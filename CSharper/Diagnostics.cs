using System;
using System.Globalization;
using Scripting.AST;

namespace Scripting.CSharper
{

/// <summary>This class represents a single diagnostic message, and contains static members for all valid messages.</summary>
public struct Diagnostic
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
    int[] exceptions = new int[] { 1003 };
    Array.Sort(exceptions);
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
        else if(Array.BinarySearch(exceptions, diag.code) < 0)
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

  /// <summary>The numeric code of this diagnostic message.</summary>
  public int Code
  {
    get { return code; }
  }

  /// <summary>The level of the diagonostic, with higher levels representing less severe issues.</summary>
  public int Level
  {
    get { return level; }
  }

  /// <summary>The format string for the diagnostic's message.</summary>
  public string Format
  {
    get { return format; }
  }

  /// <summary>The type of diagnostic message.</summary>
  public OutputMessageType Type
  {
    get { return type; }
  }

  /// <summary>Converts this diagnostic to an <see cref="OutputMessage"/>.</summary>
  /// <param name="treatWarningAsError">Whether a warning should be treated as an error.</param>
  /// <param name="sourceName">The name of the source file to which the diagnostic applies.</param>
  /// <param name="position">The position within the source file of the construct that caused the diagnostic.</param>
  /// <param name="args">Arguments to use when formatting the diagnostic's message.</param>
  public OutputMessage ToMessage(bool treatWarningAsError,
                                 string sourceName, Position position, params object[] args)
  {
    OutputMessageType type = this.type == OutputMessageType.Warning && treatWarningAsError
      ? OutputMessageType.Error : this.type;
    return new OutputMessage(type, ToString(treatWarningAsError, args), sourceName, position);
  }

  /// <summary>Converts this diagnostic to a message suitable for use in an <see cref="OutputMessage"/>.</summary>
  /// <param name="treatWarningAsError">Whether a warning should be treated as an error.</param>
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

  /// <summary>Converts this diagnostic to a message suitable for use in an <see cref="OutputMessage"/>.</summary>
  /// <param name="treatWarningAsError">Whether a warning should be treated as an error.</param>
  /// <param name="args">Arguments to use when formatting the diagnostic's message.</param>
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
      case '\0': return @"\0";
      case '\a': return @"\a";
      case '\b': return @"\b";
      case '\f': return @"\f";
      case '\n': return @"\n";
      case '\r': return @"\r";
      case '\t': return @"\t";
      case '\v': return @"\v";
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

  #region Diagnostics
  #pragma warning disable 1591 // no XML comments
  public static readonly Diagnostic UseUppercaseL           = Warning(78,   4, "The lowercase 'l' suffix is easily confused with the digit '1' -- use 'L' for clarity");
  public static readonly Diagnostic NoTypesInInterfaces       = Error(524, "Interfaces cannot declare nested types");
  public static readonly Diagnostic NoFieldsInInterfaces      = Error(525, "Interfaces cannot contain fields");
  public static readonly Diagnostic NoConstructorInInterface  = Error(526, "Interfaces cannot contain constructors");
  public static readonly Diagnostic NoDestructorOutsideClass  = Error(575, "Only class types can contain destructors");
  public static readonly Diagnostic RealConstantTooLarge      = Error(594,  "Floating-point constant is outside the range of type '{0}'");
  public static readonly Diagnostic InvalidAttributeTarget  = Warning(657,  1, "'{0}' is not a valid attribute location for this declaration. Valid attribute locations for this declaration are '{1}'. This attribute will be ignored.");
  public static readonly Diagnostic UnknownAttributeTarget  = Warning(658,  1, "'{0}' is not a recognized attribute location. All attributes in this block will be ignored.");
  public static readonly Diagnostic ExpectedIdentifier        = Error(1001, "Identifier expected");
  public static readonly Diagnostic ExpectedSemicolon         = Error(1002, "; expected");
  public static readonly Diagnostic ExpectedCharacter         = Error(1003, "Expected character '{0}'");
  public static readonly Diagnostic ExpectedSyntax            = Error(1003, "Syntax error, '{0}' expected");
  public static readonly Diagnostic DuplicateModifier         = Error(1004, "Duplicate '{0}' modifier");
  public static readonly Diagnostic ExpectedEnumType          = Error(1008, "Type byte, sbyte, short, ushort, int, uint, long, or ulong expected");
  public static readonly Diagnostic UnrecognizedEscape        = Error(1009, "Unrecognized escape sequence starting with '{0}'");
  public static readonly Diagnostic NewlineInConstant         = Error(1010, "Newline in constant");
  public static readonly Diagnostic EmptyCharacterLiteral     = Error(1011, "Empty character literal");
  public static readonly Diagnostic CharacterLiteralTooLong   = Error(1012, "Too many characters in character literal");
  public static readonly Diagnostic InvalidNumber             = Error(1013, "Invalid number");
  public static readonly Diagnostic NamedArgumentExpected     = Error(1016, "Named argument expected -- ensure that named arguments come last");
  public static readonly Diagnostic IntegralConstantTooLarge  = Error(1021, "Integral constant is too large");
  public static readonly Diagnostic PPDirectiveExpected       = Error(1024, "Preprocessor directive expected");
  public static readonly Diagnostic PPEndExpected             = Error(1025, "Single-line comment or end-of-line expected");
  public static readonly Diagnostic ExpectedClosingParen      = Error(1026, ") expected");
  public static readonly Diagnostic PPEndIfExpected           = Error(1027, "#endif directive expected");
  public static readonly Diagnostic UnexpectedPPDirective     = Error(1028, "Unexpected preprocessor directive");
  public static readonly Diagnostic UserError                 = Error(1029, "#error: '{0}'");
  public static readonly Diagnostic UserWarning             = Warning(1030, 1, "#warning: '{0}'");
  public static readonly Diagnostic PPTooLate                 = Error(1032, "Cannot define/undefine preprocessor symbols after first token in file");
  public static readonly Diagnostic UnterminatedComment       = Error(1035, "Unterminated multiline comment");
  public static readonly Diagnostic EndRegionExpected         = Error(1038, "#endregion directive expected");
  public static readonly Diagnostic UnterminatedStringLiteral = Error(1039, "Unterminated string literal");
  public static readonly Diagnostic PPNotFirstToken           = Error(1040, "Preprocessor directives must appear as the first non-whitespace character on a line");
  public static readonly Diagnostic ExpectedIdentGotKeyword   = Error(1041, "Expected an identifier, but got keyword '{0}'");
  public static readonly Diagnostic UnexpectedCharacter       = Error(1056, "Unexpected character '{0}'");
  public static readonly Diagnostic ExpectedClosingCurly      = Error(1513, "} expected");
  public static readonly Diagnostic ExpectedOpenCurly         = Error(1514, "{ expected");
  public static readonly Diagnostic InvalidPPExpression       = Error(1517, "Invalid preprocessor expression");
  public static readonly Diagnostic ExpectedTypeDeclaration   = Error(1518, "Expected class, delegate, enum, interface, or struct");
  public static readonly Diagnostic InvalidTokenInTypeDecl    = Error(1519, "Invalid token '{0}' in class, struct, or interface member declaration");
  public static readonly Diagnostic InvalidLineDirective      = Error(1576, "The #line directive is invalid");
  public static readonly Diagnostic MisplacedXmlComment     = Warning(1587, 2, "XML comment is not placed on a valid language element");
  public static readonly Diagnostic MisplacedVerbatim         = Error(1646, "Keyword, identifier, or string expected after verbatim specifier: @");
  public static readonly Diagnostic UnrecognizedPragma      = Warning(1633, 1, "Unrecognized #pragma directive");
  public static readonly Diagnostic InvalidWarningPragma    = Warning(1634, 1, "Expected format #pragma warning disable|restore n,n,...");
  public static readonly Diagnostic InvalidWarningCode      = Warning(1691, 1, "'{0}' is not a valid warning number");
  #pragma warning restore 1591

  static Diagnostic Error(int code, string format)
  {
    return new Diagnostic(OutputMessageType.Error, code, 0, format);
  }

  static Diagnostic Warning(int code, int level, string format)
  {
    return new Diagnostic(OutputMessageType.Warning, code, 1, format);
  }
  #endregion

  static int[] warningCodes;
}

} // namespace Scripting.CSharper