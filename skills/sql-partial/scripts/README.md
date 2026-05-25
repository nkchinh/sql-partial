# SQL Partial Scripts

Utility script for validating SQL Partial files.

## validate_sql.py

Validates SQL Partial files for correct structure and syntax.

### Usage

```bash
# Validate a single file
python validate_sql.py CreateProductRequestHandler.Command.sql

# Validate all SQL files in a directory
python validate_sql.py Features/Products

# Validate entire application
python validate_sql.py src/Application
```

### Validation Checks

- **File naming convention**: `{ClassName}.{Suffix}.sql`
- **PascalCase**: Class name and suffix must be PascalCase
- **Testpart syntax**: `-- #testpart` / `-- /testpart` directives
- **Testpart pairing**: Start and end directives must match
- **Basic SQL syntax**: Unclosed strings detection

### Example Output

```
✅ CreateProductRequestHandler.Command.sql
✅ GetProductRequestHandler.Query.sql
❌ InvalidFile.sql
   Line 0: Invalid naming: 'InvalidFile'. Expected format: {ClassName}.{Suffix}.sql
❌ UpdateHandler.Update.sql
   Line 15: #testpart directive without matching /testpart

Validation complete: 2/4 files valid
```

## Requirements

Python 3.6 or higher. No additional dependencies needed.

## Use Cases

### Pre-commit Validation

Validate SQL files before committing:

```bash
python validate_sql.py src/Application
```

### CI/CD Pipeline

Add to your build pipeline to ensure SQL file quality:

```yaml
# .github/workflows/build.yml
- name: Validate SQL Partial files
  run: python .skills/dotnet/sql-partial/scripts/validate_sql.py src/Application
```

### Code Review

Validate specific files during code review:

```bash
python validate_sql.py Features/Products/CreateProductRequestHandler.Command.sql
```

