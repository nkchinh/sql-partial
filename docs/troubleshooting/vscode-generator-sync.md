# Technical Note: Generator Sync Issue in VS Code (C# Dev Kit)

## Problem Description
The `SqlPartial.Generator` fails to trigger incremental updates when `.sql` files are added, modified, or deleted while working in **VS Code with C# Dev Kit**. However, the same logic works perfectly in **Visual Studio**.

### Symptoms
- Modifying a `.sql` file (inside the property-mapped area) does not update IntelliSense in C#.
- Adding a new `.sql` file does not result in a new generated property until a `.cs` file is modified or the Language Server is restarted.
- The issue persists even if the changes are made to the actual SQL content (not comments/excludes).

## Root Cause Analysis (Hypothesis)
The core of the issue lies in how the **Roslyn Language Server (LSP)** used by VS Code handles `AdditionalFiles` metadata compared to the full MSBuild integration in Visual Studio.

1. **Metadata Stripping in LSP**: C# Dev Kit's design-time analysis often passes `AdditionalText` objects to the generator but fails to populate the `AnalyzerConfigOptionsProvider` with custom item metadata (`SourceItemType`).
2. **Filter Failure**: In `SqlPartialGenerator.cs`, we filter files using:
   ```csharp
   options.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles.SourceItemType", out var itemType);
   if (itemType != "SqlPartial") return; // Fails in VS Code because itemType is null
   ```
3. **In-Memory Cache Stale**: Because the filter returns `false` due to missing metadata, the Generator pipeline thinks there are no valid SQL files, keeping the previous (or empty) state in the IDE's memory.

## Technical Details
- **Environment**: Win32, VS Code + C# Dev Kit (latest).
- **Tooling**: Roslyn Incremental Generators.
- **Trigger**: `context.AdditionalTextsProvider`.

## Potential Solutions

### Hướng 1: Thay thế Metadata bằng Naming Convention (Khả thi nhất)
Thay vì lọc nghiêm ngặt bằng `SourceItemType`, Generator sẽ chấp nhận mọi file `.sql` đi qua `AdditionalTextsProvider` và dùng logic `FilePathParser` để xác định xem nó có thuộc về một Class nào không.
- **Ưu điểm**: Đuôi file và tên file luôn khả dụng trong mọi IDE.
- **Rủi ro**: Hiệu năng có thể giảm nhẹ nếu có quá nhiều file `.sql` không liên quan trong dự án (nhưng đã được giới hạn bởi `AdditionalFiles` trong `.csproj`).

### Hướng 2: Bổ sung "LSP-friendly" Configuration
Sử dụng `CompilerVisibleProperty` (Project-level) để khai báo các folder chứa SQL thay vì gắn metadata vào từng file.
```xml
<PropertyGroup>
  <SqlPartialIncludePaths>./Data/Queries;./Storage</SqlPartialIncludePaths>
</PropertyGroup>
```

### Hướng 3: Force Watcher (Cấu hình MSBuild)
Thử nghiệm thêm `Watch="true"` vào `AdditionalFiles` trong `.targets` để gợi ý cho LSP theo dõi file chặt chẽ hơn.

## Next Steps for Implementation
1. Thử nghiệm bỏ bước lọc `SourceItemType` trong một branch riêng và kiểm tra trên VS Code.
2. Nếu thành công, refactor lại pipeline để ưu tiên xác thực qua Naming Convention.
3. Cập nhật tài liệu `troubleshooting.md` cho người dùng VS Code.
