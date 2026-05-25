# Architecture — TD.SqlPartial.Generator

Tài liệu này mô tả kiến trúc nội bộ của generator dành cho maintainer và contributor.

---

## Tổng quan

Generator là một **Roslyn Incremental Source Generator** (`IIncrementalGenerator`). Toàn bộ quá trình sinh code xảy ra bên trong Roslyn compiler pipeline — không có file `.cs` nào được ghi ra disk trong quá trình build thông thường (chỉ tồn tại dưới dạng virtual source trong bộ nhớ của Language Server, hoặc trong `obj/` khi `EmitCompilerGeneratedFiles=true`).

---

## Cấu trúc thư mục

```
TD.SqlPartial.Generator/
├── SqlPartialGenerator.cs          Entry point — khai báo pipeline
├── Core/
│   ├── ConfigParser.cs             Đọc MSBuild properties → GeneratorConfig
│   ├── FilePathParser.cs           Parse đường dẫn file → (ns, className, queryName, slug)
│   ├── SqlContentCleaner.cs        Strip comments, testpart blocks, escape verbatim strings
│   └── SourceBuilder.cs            Sinh C# source từ models
├── Models/
│   ├── GeneratorConfig.cs          Cấu hình cấp project (providers, namespaces...)
│   ├── SqlProvider.cs              Một DBMS provider (slug + display name)
│   ├── SqlFile.cs                  Một file .sql đã parse
│   └── SqlQueryGroup.cs            Nhóm các SqlFile cùng (ns, className, queryName)
└── build/
    └── TD.SqlPartial.Generator.targets   MSBuild targets đóng gói trong NuGet
```

---

## Pipeline generator

```
AdditionalTextsProvider
    │ filter: .sql + SourceItemType=SqlPartial
    │ select: đọc content, clean
    ▼
(FilePath, Content)
    │ combine với GeneratorConfig + ProjectDir
    │ select: FilePathParser.TryParse → SqlFile
    ▼
ImmutableArray<SqlFile>  ← Collect()
    │ SelectMany: group theo (Namespace, ClassName, QueryName)
    ▼
ImmutableArray<SqlQueryGroup>  ← Collect()
    │ combine với GeneratorConfig
    │ SelectMany: group theo (Namespace, ClassName)
    ▼
classBatches → RegisterSourceOutput → SourceBuilder.BuildPartialClass()

AnalyzerConfigOptionsProvider
    │ select: ConfigParser.Parse → GeneratorConfig
    ▼
GeneratorConfig → RegisterSourceOutput → SourceBuilder.BuildSqlStringsStruct()
```

### Tại sao dùng `Collect()` hai lần

Bước đầu `Collect()` gom tất cả `SqlFile` để có thể group theo query. Nếu không collect trước, mỗi `SqlFile` sẽ được xử lý độc lập và không thể biết các file của cùng một query.

Bước hai `Collect()` gom tất cả `SqlQueryGroup` để group theo class, từ đó sinh một file `.g.cs` duy nhất per class thay vì một file per query.

**Nhược điểm**: mỗi khi bất kỳ `.sql` nào thay đổi, toàn bộ pipeline sau `Collect()` đều re-execute. Đây là đánh đổi cần thiết vì grouping là phép toán toàn cục. Với số lượng `.sql` file thực tế trong một project, chi phí này không đáng kể.

---

## Models

### `GeneratorConfig`

Parsed từ MSBuild global properties một lần duy nhất. Stable giữa các lần chỉnh sửa `.sql` — Roslyn cache lại và không re-parse trừ khi project file thay đổi.

| Property | MSBuild source | Mô tả |
|---|---|---|
| `RootNamespace` | `build_property.RootNamespace` | Namespace gốc của project |
| `Providers` | `build_property.SqlPartialProviders` | Danh sách DBMS provider |
| `SqlStringsNamespace` | `build_property.SqlPartialStringsNamespace` | Namespace cho struct `SqlStrings` |
| `ExternalSqlStringsType` | `build_property.SqlPartialStringsType` | Dùng struct từ assembly khác |
| `NullableEnabled` | `build_property.Nullable` | Có emit `#nullable enable` không |

### `SqlFile`

Đại diện cho một file `.sql` sau khi parse. Implement `IEquatable<SqlFile>` — bắt buộc để Roslyn incremental caching hoạt động đúng. Nếu không có `IEquatable`, pipeline luôn re-execute dù content không đổi.

### `SqlQueryGroup`

Nhóm tất cả `SqlFile` cùng `(Namespace, ClassName, QueryName)`. Chứa `ImmutableDictionary<string, string>` ánh xạ slug → SQL content.

Phương thức `GetContent(slug)`:
```
slug tồn tại trong dict → trả về content của slug đó
slug không tồn tại       → fallback về "an" (ANSI)
không có "an"            → trả về string.Empty
```

---

## Targets và cơ chế trigger

### Tại sao khai báo `AdditionalFiles` trực tiếp

Các generator khác (kể cả phiên bản cũ của project này) thường dùng custom item type (`<SqlPartial>`) rồi transform sang `<AdditionalFiles>`. Cách này phá vỡ cơ chế file-watching của Roslyn Language Server trong VSCode/C# DevKit: Language Server chỉ watch các `AdditionalFiles` được khai báo trực tiếp, không watch qua item type trung gian.

Giải pháp: user khai báo `<AdditionalFiles>` trực tiếp với metadata `<SourceItemType>SqlPartial</SourceItemType>`. Language Server thấy file path trực tiếp và thiết lập watcher đúng cách — generator tự động trigger khi lưu file `.sql`.

### `CompilerVisibleProperty` và `CompilerVisibleItemMetadata`

Các khai báo này trong `.targets` cho phép generator đọc MSBuild properties/metadata thông qua `AnalyzerConfigOptionsProvider`. Không có chúng, `TryGetValue("build_property.XYZ")` luôn trả về `false`.

Đây là implementation detail của package — người dùng không cần và không nên tự khai báo.

### `UpToDateCheckInput`

Đảm bảo MSBuild Fast Up-To-Date Check (FUTDC) phát hiện thay đổi `.sql` và trigger build đầy đủ. Không có dòng này, FUTDC có thể bỏ qua build dù `.sql` đã thay đổi.

---

## Sinh code — `SourceBuilder`

### `SqlStrings` struct

Sinh một lần per project. Struct là `readonly` để đảm bảo immutability. Property `AnsiSql` luôn có mặt; các provider property là `string?` để phân biệt "chưa có SQL riêng" với "SQL rỗng".

Phương thức `Get(string providerName)` dùng `switch` trên display name (không phải slug) vì đây là giá trị người dùng cấu hình trong appsettings — ví dụ `"PostgreSql"` chứ không phải `"pg"`.

### Partial class

Mỗi `(Namespace, ClassName)` → một file hint `ClassName.{hash}.g.cs`. Hash được tính từ fully-qualified class name để tránh collision khi hai class cùng tên ở namespace khác nhau.

Property được sinh là `private static readonly` — không phải `const` vì `readonly struct` không thể là `const`.

---

## Quy ước tên file `.sql`

```
ClassName.QueryName.sql          → providerSlug = "an"
ClassName.QueryName.an.sql       → providerSlug = "an"  (explicit)
ClassName.QueryName.pg.sql       → providerSlug = "pg"
```

`FilePathParser.TryParse` bóc tách theo quy ước này. File không khớp pattern (ít hơn 2 segment trước `.sql`) bị bỏ qua — không gây lỗi build.

---

## Xử lý SQL content — `SqlContentCleaner`

Thực hiện theo thứ tự:
1. Xóa block `--#testpart … --/testpart` (case-insensitive)
2. Xóa dòng trống và dòng comment (`--`)
3. Escape `"` thành `""` cho C# verbatim string literal (`@"..."`)

---

## Đóng gói NuGet

File `.targets` phải nằm ở đường dẫn `build/TD.SqlPartial.Generator.targets` trong NuGet package để được tự động import. Cấu hình trong `.csproj` của generator project:

```xml
<ItemGroup>
    <None Include="build\TD.SqlPartial.Generator.targets" Pack="true"
          PackagePath="build\" />
</ItemGroup>
```

Generator assembly phải được đánh dấu là analyzer:

```xml
<ItemGroup>
    <None Include="$(OutputPath)\$(AssemblyName).dll" Pack="true"
          PackagePath="analyzers\dotnet\cs\" Visible="false" />
</ItemGroup>
```

---

## Thêm provider mới

Không cần thay đổi code generator. Người dùng chỉ cần thêm vào `SqlPartialProviders`:

```xml
<SqlPartialProviders>pg:PostgreSql;ms:SqlServer;ora:Oracle</SqlPartialProviders>
```

Generator sinh thêm property `Oracle` trên `SqlStrings` và case `"Oracle"` trong `Get()` tự động.

---

## Chạy test thủ công

Bật `EmitCompilerGeneratedFiles` để xem file được sinh ra:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

File xuất hiện tại `obj/Debug/{tfm}/generated/TD.SqlPartial.Generator/`.
