using System;
using System.Globalization;
using Scripting.AST;

namespace Scripting.CSharper
{

struct Diagnostic
{
  Diagnostic(OutputMessageType type, int code, int level, string format)
  {
    if(code < 0 || code > 9999) throw new ArgumentOutOfRangeException(); // should be a 4-digit code
    if(string.IsNullOrEmpty(format)) throw new ArgumentException();

    this.type   = type;
    this.code   = code;
    this.level  = level;
    this.format = format;
  }

  static Diagnostic()
  {
    // verify that all diagnostic codes are unique, and gather a list of warning codes
    #if DEBUG
    System.Collections.Generic.List<int> codes = new System.Collections.Generic.List<int>();
    #endif
    System.Collections.Generic.List<int> warnings = new System.Collections.Generic.List<int>();
    foreach(System.Reflection.FieldInfo field in typeof(Diagnostic).GetFields())
    {
      if(field.IsStatic && field.IsPublic && field.FieldType == typeof(Diagnostic))
      {
        Diagnostic diag = (Diagnostic)field.GetValue(null);
        #if DEBUG
        int index = codes.BinarySearch(diag.code);
        if(index <= 0)
        {
          codes.Insert(~index, diag.code);
        }
        else
        {
          throw new InvalidOperationException("Duplicated diagnostic codes!");
        }
        #endif

        if(diag.Type == OutputMessageType.Warning) warnings.Add(diag.code);
      }
    }

    warnings.Sort();
    Diagnostic.warningCodes = warnings.ToArray();
  }

  public int Code
  {
    get { return code; }
  }

  public int Level
  {
    get { return level; }
  }

  public string Format
  {
    get { return format; }
  }

  public OutputMessageType Type
  {
    get { return type; }
  }

  public OutputMessage ToMessage(bool treatWarningAsError,
                                 string sourceName, FilePosition position, params object[] args)
  {
    return new OutputMessage(type, ToString(treatWarningAsError, args), sourceName, position);
  }

  public string ToString(bool treatWarningAsError)
  {
    string typeString;
    if(type == OutputMessageType.Error || treatWarningAsError && type == OutputMessageType.Warning)
    {
      typeString = "error";
    }
    else if(type == OutputMessageType.Warning)
    {
      typeString = "warning";
    }
    else
    {
      typeString = "information";
    }

    return string.Format(CultureInfo.InvariantCulture, "{0} CS{1:D4}: {2}", typeString, code, format);
  }

  public string ToString(bool treatWarningAsError, params object[] args)
  {
    return string.Format(CultureInfo.InvariantCulture, ToString(treatWarningAsError), args);
  }

  readonly string format;
  readonly OutputMessageType type;
  readonly int code, level;

  /// <summary>Returns a name for the given character suitable for insertion between single quotes.</summary>
  public static string CharLiteral(char c)
  {
    switch(c)
    {
      case '\'': return @"\'";
      case '\\': return @"\\";
      case '\0':  return @"\0";
      case '\a':  return @"\a";
      case '\b':  return @"\b";
      case '\f':  return @"\f";
      case '\n':  return @"\n";
      case '\r':  return @"\r";
      case '\t':  return @"\t";
      case '\v':  return @"\v";
      default:
        return c < 32 || c > 126 ? "0x"+((int)c).ToString("X") : new string(c, 1);
    }
  }

  /// <summary>Determines whether the given warning code refers to a valid warning.</summary>
  public static bool IsValidWarning(int code)
  {
    return Array.BinarySearch(warningCodes, code) >= 0;
  }

  /// <summary>Returns a name for the given type suitable for use in diagnostic messages.</summary>
  public static string TypeName(Type type)
  {
    if(type == null) throw new ArgumentNullException();

    switch(System.Type.GetTypeCode(type))
    {
      case TypeCode.Boolean: return "bool";
      case TypeCode.Byte: return "byte";
      case TypeCode.Char: return "char";
      case TypeCode.Decimal: return "decimal";
      case TypeCode.Double: return "double";
      case TypeCode.Int16: return "short";
      case TypeCode.Int32: return "int";
      case TypeCode.Int64: return "long";
      case TypeCode.SByte: return "sbyte";
      case TypeCode.Single: return "float";
      case TypeCode.String: return "string";
      case TypeCode.UInt16: return "ushort";
      case TypeCode.UInt32: return "uint";
      case TypeCode.UInt64: return "ulong";
      default:
        if(type == typeof(object)) return "object";
        else if(type.IsArray) return TypeName(type.GetElementType())+"["+new string(',', type.GetArrayRank()-1)+"]";
        else if(type.IsPointer) return TypeName(type.GetElementType())+"*";
        else if(type.IsByRef) return TypeName(type.GetElementType())+"&";
        else return type.FullName;
    }
  }

  public static readonly Diagnostic UseUppercaseL =
    Warning(78, 4, "The 'l' suffix is easily confused with the digit '1' -- use 'L' for clarity");
  public static readonly Diagnostic RealConstantTooLarge =
    Error(594, "Floating-point constant is outside the range of type '{0}'");
  public static readonly Diagnostic ExpectedIdentifier = Error(1001, "Identifier expected");
  public static readonly Diagnostic ExpectedCharacter = Error(1003, "Expected character '{0}'");
  public static readonly Diagnostic UnrecognizedEscape = Error(1009, "Unrecognized escape sequence starting with '{0}'");
  public static readonly Diagnostic NewlineInConstant = Error(1010, "Newline in constant");
  public static readonly Diagnostic EmptyCharacterLiteral = Error(1011, "Empty character literal");
  public static readonly Diagnostic CharacterLiteralTooLong = Error(1012, "Too many characters in character literal");
  public static readonly Diagnostic InvalidNumber = Error(1013, "Invalid number");
  public static readonly Diagnostic IntegralConstantTooLarge = Error(1021, "Integral constant is too large");
  public static readonly Diagnostic PPDirectiveExpected = Error(1024, "Preprocessor directive expected");
  public static readonly Diagnostic PPEndExpected = Error(1025, "Single-line comment or end-of-line expected");
  public static readonly Diagnostic PPEndIfExpected = Error(1027, "#endif directive expected");
  public static readonly Diagnostic UnexpectedPPDirective = Error(1028, "Unexpected preprocessor directive");
  public static readonly Diagnostic PPTooLate =
    Error(1032, "Cannot define/undefine preprocessor symbols after first token in file");
  public static readonly Diagnostic UnterminatedComment = Error(1035, "Unterminated multiline comment");
  public static readonly Diagnostic EndRegionExpected = Error(1038, "#endregion directive expected");
  public static readonly Diagnostic UnterminatedStringLiteral = Error(1039, "Unterminated string literal");
  public static readonly Diagnostic PPNotFirstToken =
    Error(1040, "Preprocessor directives must appear as the first non-whitespace character on a line");
  public static readonly Diagnostic UnexpectedCharacter = Error(1056, "Unexpected character '{0}'");
  public static readonly Diagnostic InvalidPPExpression = Error(1517, "Invalid preprocessor expression");
  public static readonly Diagnostic InvalidLineDirective = Error(1576, "The #line directive is invalid");
  public static readonly Diagnostic MisplacedVerbatim =
    Error(1646, "Keyword, identifier, or string expected after verbatim specifier: @");
  public static readonly Diagnostic UnrecognizedPragma = Warning(1633, 1, "Unrecognized #pragma directive");
  public static readonly Diagnostic InvalidWarningPragma = Warning(1634, 1, "Expected format #pragma warning disable|restore n,n,...");
  public static readonly Diagnostic InvalidWarningCode = Warning(1691, 1, "'{0}' is not a valid warning number");

  static Diagnostic Error(int code, string format)
  {
    return new Diagnostic(OutputMessageType.Error, code, 0, format);
  }

  static Diagnostic Warning(int code, int level, string format)
  {
    return new Diagnostic(OutputMessageType.Warning, code, 1, format);
  }

  static int[] warningCodes;
}

} // namespace Scripting.CSharper