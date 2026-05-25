#!/usr/bin/env python3
"""
SQL Partial File Validator

Validates SQL Partial files for:
- Correct naming convention ({ClassName}.{Suffix}.sql)
- Proper testpart directive syntax
- SQL syntax basics
- File structure

Usage:
    python validate_sql.py <sql_file_path>
    python validate_sql.py <directory_path>  # Validates all .sql files in directory
"""

import sys
import re
from pathlib import Path
from typing import List, Tuple


class ValidationError:
    def __init__(self, line_number: int, message: str):
        self.line_number = line_number
        self.message = message

    def __str__(self):
        if self.line_number > 0:
            return f"Line {self.line_number}: {self.message}"
        return self.message


def validate_file_naming(file_path: Path) -> List[ValidationError]:
    """Validate file naming convention: {ClassName}.{Suffix}.sql"""
    errors = []
    
    filename = file_path.stem  # Without .sql extension
    parts = filename.split('.')
    
    if len(parts) < 2:
        errors.append(ValidationError(
            0, 
            f"Invalid naming: '{filename}'. Expected format: {{ClassName}}.{{Suffix}}.sql"
        ))
        return errors
    
    class_name = parts[0]
    suffix = '.'.join(parts[1:])
    
    # Validate class name (PascalCase)
    if not re.match(r'^[A-Z][a-zA-Z0-9]*$', class_name):
        errors.append(ValidationError(
            0,
            f"Class name '{class_name}' should be PascalCase (e.g., CreateReportRequestHandler)"
        ))
    
    # Validate suffix (PascalCase)
    if not re.match(r'^[A-Z][a-zA-Z0-9]*$', suffix):
        errors.append(ValidationError(
            0,
            f"Suffix '{suffix}' should be PascalCase (e.g., Command, Query, Update)"
        ))
    
    return errors


def validate_testpart_syntax(content: str) -> List[ValidationError]:
    """Validate testpart directive syntax"""
    errors = []
    lines = content.split('\n')
    
    testpart_start = None
    testpart_end = None
    
    for i, line in enumerate(lines, 1):
        stripped = line.strip()
        
        # Check for testpart start
        if re.match(r'^--\s*#testpart\s*$', stripped):
            if testpart_start is not None:
                errors.append(ValidationError(
                    i,
                    "Duplicate #testpart directive (previous at line {})".format(testpart_start)
                ))
            testpart_start = i
        
        # Check for testpart end
        elif re.match(r'^--\s*/testpart\s*$', stripped):
            if testpart_end is not None:
                errors.append(ValidationError(
                    i,
                    "Duplicate /testpart directive (previous at line {})".format(testpart_end)
                ))
            testpart_end = i
        
        # Check for malformed testpart directives
        elif '#testpart' in stripped or '/testpart' in stripped:
            if not re.match(r'^--\s*[#/]testpart\s*$', stripped):
                errors.append(ValidationError(
                    i,
                    "Malformed testpart directive. Use: '-- #testpart' or '-- /testpart'"
                ))
    
    # Check pairing
    if testpart_start is not None and testpart_end is None:
        errors.append(ValidationError(
            testpart_start,
            "#testpart directive without matching /testpart"
        ))
    
    if testpart_end is not None and testpart_start is None:
        errors.append(ValidationError(
            testpart_end,
            "/testpart directive without matching #testpart"
        ))
    
    if testpart_start is not None and testpart_end is not None:
        if testpart_end <= testpart_start:
            errors.append(ValidationError(
                testpart_end,
                "/testpart must come after #testpart"
            ))
    
    return errors


def validate_sql_basics(content: str) -> List[ValidationError]:
    """Basic SQL syntax validation"""
    errors = []
    lines = content.split('\n')
    
    # Check for unclosed strings
    in_string = False
    string_char = None
    
    for i, line in enumerate(lines, 1):
        # Skip comments
        if line.strip().startswith('--'):
            continue
        
        for char in line:
            if char in ("'", '"') and not in_string:
                in_string = True
                string_char = char
            elif char == string_char and in_string:
                in_string = False
                string_char = None
    
    if in_string:
        errors.append(ValidationError(
            0,
            f"Unclosed string literal (started with {string_char})"
        ))
    
    return errors


def validate_file(file_path: Path) -> Tuple[bool, List[ValidationError]]:
    """Validate a single SQL Partial file"""
    errors = []
    
    # Check file exists
    if not file_path.exists():
        errors.append(ValidationError(0, f"File not found: {file_path}"))
        return False, errors
    
    # Check file extension
    if file_path.suffix.lower() != '.sql':
        errors.append(ValidationError(0, "File must have .sql extension"))
        return False, errors
    
    # Validate naming
    errors.extend(validate_file_naming(file_path))
    
    # Read content
    try:
        content = file_path.read_text(encoding='utf-8')
    except Exception as e:
        errors.append(ValidationError(0, f"Error reading file: {e}"))
        return False, errors
    
    # Validate testpart syntax
    errors.extend(validate_testpart_syntax(content))
    
    # Validate SQL basics
    errors.extend(validate_sql_basics(content))
    
    return len(errors) == 0, errors


def main():
    if len(sys.argv) != 2:
        print("Usage: python validate_sql.py <sql_file_or_directory>")
        sys.exit(1)
    
    path = Path(sys.argv[1])
    
    if path.is_file():
        files = [path]
    elif path.is_directory():
        files = list(path.rglob('*.*.sql'))
        if not files:
            print(f"No SQL Partial files found in {path}")
            sys.exit(0)
    else:
        print(f"Error: {path} is not a file or directory")
        sys.exit(1)
    
    total_files = len(files)
    valid_files = 0
    
    for file_path in files:
        is_valid, errors = validate_file(file_path)
        
        if is_valid:
            print(f"✅ {file_path.name}")
            valid_files += 1
        else:
            print(f"❌ {file_path.name}")
            for error in errors:
                print(f"   {error}")
    
    print(f"\nValidation complete: {valid_files}/{total_files} files valid")
    
    sys.exit(0 if valid_files == total_files else 1)


if __name__ == "__main__":
    main()

