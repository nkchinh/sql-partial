---
name: sql-partial
description: |
  Hướng dẫn agent tạo và quản lý SQL partial files cho dự án dùng TD.SqlPartial.Generator.
  Dùng skill này bất cứ khi nào người dùng muốn:
  - Thêm SQL query mới vào một class (tạo file .sql)
  - Thêm provider-specific SQL cho một query đã có (pg, ms, my...)
  - Cấu hình TD.SqlPartial.Generator trong .csproj
  - Kiểm tra cấu trúc file .sql có đúng convention không
  - Xem danh sách queries đang có trong project
  - Tái cấu trúc SQL files (đổi tên, di chuyển, thêm provider)
  Trigger ngay khi thấy đề cập đến: sql partial, SqlStrings, query file, .sql generator, DBMS provider, AnsiSql.
---

# TD.SqlPartial.Generator Skill

## Tổng quan nhanh

Generator biến `.sql` files thành `static readonly SqlStrings` properties prefixed với `Sql` trên `partial class`.
Người dùng gọi `MyClass.SqlGetUser.Get("PostgreSql")` tại runtime — generator lo phần còn lại.

---

## Quy ước tên file (QUAN TRỌNG)

```
ClassName.QueryName.sql          ← ANSI SQL, dùng chung cho mọi DBMS
ClassName.QueryName.an.sql       ← Giống trên, explicit
ClassName.QueryName.pg.sql       ← PostgreSQL override
ClassName.QueryName.ms.sql       ← SQL Server override
ClassName.QueryName.my.sql       ← MySQL override
```

- **ClassName** phải khớp chính xác tên `partial class` (case-sensitive)
- **QueryName** trở thành tên property trên class (được tự động prefix với `Sql`)
- **Slug** phải khớp một slug trong `SqlPartialProviders` của project, hoặc `an`

File đặt trong cùng thư mục với class → namespace khớp tự động.
File đặt trong thư mục con → namespace = `RootNamespace.TênThưMụcCon`.

---

## Workflow: thêm SQL mới

### Bước 1 — Thu thập thông tin

Hỏi người dùng (hoặc suy ra từ context) 4 thứ:
1. **ClassName** — class nào sẽ chứa query?
2. **QueryName** — tên property mong muốn (sau khi sinh sẽ có prefix `Sql`)?
3. **Provider** — ANSI chung hay provider-specific? Nếu specific thì slug nào?
4. **Nội dung SQL** — người dùng cung cấp hay cần soạn?

### Bước 2 — Xác định thư mục

```bash
# Tìm file .csproj để biết project root
find . -name "*.csproj" -not -path "*/obj/*" | head -5

# Tìm class đích để biết nó nằm ở đâu
find . -name "ClassName.cs" -not -path "*/obj/*"
```

File `.sql` phải đặt cùng thư mục với class để namespace khớp.

### Bước 3 — Kiểm tra cấu hình project

```bash
# Kiểm tra SqlPartialProviders và AdditionalFiles đã cấu hình chưa
grep -A5 "SqlPartialProviders\|SqlPartial\|AdditionalFiles.*sql" *.csproj
```

Nếu chưa có cấu hình → xem phần **Cấu hình .csproj** bên dưới.

### Bước 4 — Tạo file

Tạo file đúng tên convention, đặt đúng thư mục. SQL content cần:
- Xóa comment giải thích nếu người dùng không muốn giữ (generator tự strip `--` comments)
- Giữ `--#testpart … --/testpart` nếu có đoạn SQL chỉ dùng cho test

### Bước 5 — Xác nhận partial class tồn tại

```bash
grep -r "partial class ClassName" --include="*.cs" -l
```

Nếu chưa có → nhắc người dùng tạo hoặc thêm `partial` keyword vào class hiện tại.

---

## Cấu hình .csproj

Khi project chưa có cấu hình, thêm vào `.csproj`:

```xml
<PropertyGroup>
    <!-- Khai báo DBMS providers: slug:DisplayName, phân cách bằng ; -->
    <!-- ANSI SQL luôn có sẵn, không cần khai báo -->
    <SqlPartialProviders>pg:PostgreSql;ms:SqlServer</SqlPartialProviders>
</PropertyGroup>

<ItemGroup>
    <!-- Include trực tiếp làm AdditionalFiles — KHÔNG dùng custom item type trung gian -->
    <AdditionalFiles Include="**/*.*.sql" Exclude="obj/**/*;bin/**/*">
        <SourceItemType>SqlPartial</SourceItemType>
    </AdditionalFiles>
</ItemGroup>
```

**Lưu ý quan trọng**: pattern `**/*.*.sql` (hai dấu chấm) đảm bảo file phải có ít nhất `ClassName.QueryName.sql` — tránh nhặt file SQL không liên quan chỉ có một tên đơn.

### Tùy chọn: namespace cho SqlStrings struct

```xml
<PropertyGroup>
    <!-- Mặc định: RootNamespace -->
    <SqlPartialStringsNamespace>MyCompany.Data</SqlPartialStringsNamespace>
</PropertyGroup>
```

### Tùy chọn: dùng SqlStrings từ project khác

```xml
<PropertyGroup>
    <!-- Khi set, generator KHÔNG sinh SqlStrings struct trong project này -->
    <SqlPartialStringsType>MyCompany.Core.SqlStrings</SqlPartialStringsType>
</PropertyGroup>
```

---

## Kết quả sinh ra

Với cấu hình `SqlPartialProviders=pg:PostgreSql;ms:SqlServer` và các file:
```
Data/UserRepo.GetById.sql
Data/UserRepo.GetById.pg.sql
```

Generator sinh:

```csharp
// SqlStrings.g.cs
namespace MyApp
{
    public readonly struct SqlStrings
    {
        public string AnsiSql { get; }
        public string? PostgreSql { get; }
        public string? SqlServer { get; }

        public SqlStrings(string ansiSql, string? postgresql = null, string? sqlserver = null)
        {
            AnsiSql = ansiSql;
            PostgreSql = postgresql;
            SqlServer = sqlserver;
        }

        public string Get(string providerName) { ... }
    }
}

// UserRepo.{hash}.g.cs
namespace MyApp.Data
{
    partial class UserRepo
    {
        private static readonly SqlStrings SqlGetById = new SqlStrings(
            @"SELECT ...",   // từ GetById.sql
            postgresql: @"SELECT ..."   // từ GetById.pg.sql
            // SqlServer không có file riêng → fallback về AnsiSql tại runtime
        );
    }
}
```

Runtime:
```csharp
// providerName đọc từ appsettings, ví dụ "PostgreSql"
var sql = UserRepo.SqlGetById.Get(providerName);

// Nếu project chỉ dùng 1 DBMS, có thể ép kiểu ngầm định sang string (trả về AnsiSql)
string sqlSimple = UserRepo.SqlGetById;
```

---

## Kiểm tra và debug

### Xem tất cả SQL files hiện có

```bash
find . -name "*.*.sql" -not -path "*/obj/*" -not -path "*/bin/*" | sort
```

### Kiểm tra convention có đúng không

```bash
# File phải có ít nhất 2 segment trước .sql
# Đúng:  UserRepo.GetById.sql  |  UserRepo.GetById.pg.sql
# Sai:   GetById.sql  |  queries.sql
find . -name "*.sql" -not -path "*/obj/*" | while read f; do
    base=$(basename "$f" .sql)
    count=$(echo "$base" | tr -cd '.' | wc -c)
    if [ "$count" -lt 1 ]; then
        echo "WARN: $f — thiếu segment, sẽ bị bỏ qua bởi generator"
    fi
done
```

### Bật EmitCompilerGeneratedFiles để xem output

Thêm vào `.csproj` khi cần debug:
```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

File sinh ra tại: `obj/Debug/{tfm}/generated/TD.SqlPartial.Generator/`

### Lỗi thường gặp

| Triệu chứng | Nguyên nhân | Cách fix |
|---|---|---|
| Property không xuất hiện trên class | File `.sql` không match convention | Kiểm tra tên file có đúng `ClassName.QueryName[.slug].sql` |
| Namespace không khớp class chính | File `.sql` đặt sai thư mục | Di chuyển file `.sql` về cùng thư mục với `.cs` |
| `SqlStrings` không tìm thấy | Chưa build sau khi thêm file | Build project hoặc lưu file `.cs` bất kỳ để trigger generator |
| Provider property null | Không có file `.slug.sql` cho provider đó | Đây là behavior đúng — `Get()` tự fallback về `AnsiSql` |
| Generator không trigger khi sửa .sql | `AdditionalFiles` khai báo sai | Đảm bảo dùng `<AdditionalFiles Include="...">` trực tiếp, không qua custom item type |

---

## Ví dụ đầy đủ

**Yêu cầu**: Thêm query `GetByEmail` cho class `UserRepo`, có SQL riêng cho PostgreSQL.

**Files cần tạo**:

`Data/UserRepo.GetByEmail.sql` (ANSI fallback):
```sql
SELECT id, email, name
FROM users
WHERE email = @email
```

`Data/UserRepo.GetByEmail.pg.sql` (PostgreSQL override):
```sql
SELECT id, email, name
FROM users
WHERE email = $1
```

**Kết quả sử dụng**:
```csharp
public partial class UserRepo
{
    public User? FindByEmail(string email, string dbProvider)
    {
        var sql = SqlGetByEmail.Get(dbProvider); // "PostgreSql" hoặc bất kỳ
        // ... execute sql
    }
}
```
