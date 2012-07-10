using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Scripting.CSharper;

namespace Scripting.CSharper.Tests
{

using Token = AST.Token<TokenType,TokenData>;

[TestFixture]
public class ScannerTest
{
  #region TestErrors
  [Test]
  public void TestErrors()
  {
    Assert.AreEqual("short*", Diagnostic.TypeName(typeof(short).MakePointerType()));
    Assert.AreEqual("int**", Diagnostic.TypeName(typeof(int).MakePointerType().MakePointerType()));
    Assert.AreEqual("uint[]", Diagnostic.TypeName(typeof(uint).MakeArrayType()));
    Assert.AreEqual("long[,,]", Diagnostic.TypeName(typeof(long).MakeArrayType(3)));
    Assert.AreEqual("ulong[][]", Diagnostic.TypeName(typeof(ulong).MakeArrayType().MakeArrayType()));
    Assert.AreEqual("ushort&", Diagnostic.TypeName(typeof(ushort).MakeByRefType()));
    Assert.AreEqual("byte*[]", Diagnostic.TypeName(typeof(byte).MakePointerType().MakeArrayType()));
    Assert.AreEqual("System.DateTime", Diagnostic.TypeName(typeof(DateTime)));
    
    TestError(78, null, 1, 2, "1l");
    TestError(78, null, 4, 2, "#pragma warning disable\n#pragma warning disable 12345\n#pragma warning restore 78\n1l");
    TestValues("#pragma warning disable 78\n1l\n#pragma warning restore 78\n#pragma warning restore\n0", 1L, 0);
    TestValues("#pragma warning disable\n#pragma warning restore 78\n#pragma warning disable 78\n1l", 1L);
    TestValues("#pragma warning restore 78\n#pragma warning disable 78\n1l", 1L);

    TestError(594, "Floating-point constant is outside the range of type 'double'", 1, 41,
              "class MyClass { void Main() { float f = 6.77777777777E400; } }");
    TestError(594, "Floating-point constant is outside the range of type 'float'", 1, 41,
              "class MyClass { void Main() { float f = 6.77777777777E100f; } }");
    TestError(1001, null, 1, 9, "#define ");
    TestError(1003, @"Expected character '\''", 2, 4, "  \n 'x\n'");
    TestError(1003, @"Expected character '\''", 1, 2, "'");
    TestError(1009, "Unrecognized escape sequence starting with 'q'", 1, 7, @"""\r\n\q\p""");
    TestError(1009, "Unrecognized escape sequence starting with 'q'", 1, 4, @"'\xqqqq'");
    TestError(1010, null, 1, 3, "\"x\n");
    TestError(1011, null, 1, 2, "''");
    TestError(1012, null, 1, 4, "'xa'");
    TestError(1013, null, 1, 2, " 0xzippy");
    TestError(1021, null, 1, 1, "123456789012345678901234567890");
    TestError(1021, null, 1, 1, "0x123456789abcdef01");
    TestError(1024, null, 1, 2, "#");
    TestError(1024, null, 1, 1, "#foo");
    TestError(1025, null, 1, 13, "#define foo bar");
    TestError(1027, null, 2, 1, "#if DEBUG\n");
    TestError(1028, null, 1, 1, "#else");
    TestError(1028, null, 1, 1, "#endregion");
    TestError(1029, "#error: 'hello'", 1, 1, "#error hello");
    TestError(1030, "#warning: 'hello'", 1, 1, "#warning hello");
    TestError(1032, null, 2, 1, "5\n#define foo");
    TestError(1035, null, 1, 3, "/*");
    TestError(1038, null, 2, 1, "#region foo\n");
    TestError(1039, null, 1, 3, "\"x");
    TestError(1039, null, 1, 3, "@'");
    TestError(1040, null, 1, 3, "5 #pragma");
    TestError(1056, @"Unexpected character '\b'", 1, 1, "\b");
    TestError(1056, @"Unexpected character '\'", 1, 1, "\\");
    TestError(1056, @"Unexpected character '0x7F'", 1, 1, "\x7f");
    TestError(1517, null, 1, 1, "#if a a a");
    TestError(1576, null, 1, 1, "#line xxx");
    TestError(1646, null, 1, 2, "@ ");
    TestError(1633, null, 1, 1, "#pragma hello");
    TestError(1634, null, 1, 1, "#pragma warning foo");
    TestError(1691, "'12345' is not a valid warning number", 1, 1, "#pragma warning disable 1633, 12345");
  }

  static void TestError(int errorCode, string message, int line, int column, string code)
  {
    Compiler compiler = new Compiler();
    Scanner scanner = new Scanner(compiler, new StringReader(code));
    Token token;
    while(scanner.NextToken(out token)) { }

    Assert.GreaterOrEqual(compiler.Messages.Count, 1, "Expected a "+errorCode.ToString()+" diagnostic code");
    Assert.AreEqual(line, compiler.Messages[0].Position.Line);
    Assert.AreEqual(column, compiler.Messages[0].Position.Column);

    string errorLine = compiler.Messages[0].Message;
        
    Match m = errorRe.Match(errorLine);
    int actualCode;
    int.TryParse(m.Groups[1].Value, out actualCode);
    Assert.AreEqual(errorCode, actualCode);
    if(message != null) Assert.AreEqual(message, m.Success ? m.Groups[2].Value : errorLine);
  }

  static readonly Regex errorRe = new Regex(@"^(?:error|warning) CS(\d{4}): (.*)$", RegexOptions.Singleline);
  #endregion

  #region TestPP
  [Test]
  public void TestPP()
  {
    TestError(1517, null, 2, 1, "#define FOO\n#if 11 FOO");
    TestError(1517, null, 1, 1, "#if FOO &&");
    TestError(1517, null, 1, 1, "#if (FOO");
    TestError(1028, null, 3, 1, "#if true\n#else\n#else\n#endif");
    TestTokens("#define \\u46O今夜OA\n#if FO今夜O\\u41\nvoid\n#endif", TokenType.Void);
    TestValues("#define YES\n#if true\n#if YES && NO\n1\n#elif NO || YES\n2\n#else\n3\n#endif\n#endif\n@'done'",
               2, "done");
    TestValues("#define YES\n#if YES == true\n1\n#endif\n#if NO != false\n0\n#else\n2\n#endif", 1, 2);
    TestValues("#define YES\n#if NO && NO || YES\n1\n#endif\n#if NO && (NO || YES)\n2\n#endif", 1);
    TestValues("#define YES\n#if !YES == false && !!YES == YES // bar\n1\n#endif // foo", 1);
    TestValues("#if false\n#if FOO\nfoo\n#elsif TRUE\nbar\n#else\nbaz\n#endif\n2\n#endif\n1", 1);
    TestValues("#undef FOO\n#define YES\n#undef YES\n#if YES\ntrue\n#else\nfalse\n#endif", false);
  }
  #endregion

  #region TestTokens
  [Test]
  public void TestTokens()
  {
    TestTokens(@"abc @void vo\u0069d \u0069 get a_b void",
               TokenType.Identifier, TokenType.Identifier, TokenType.Identifier, TokenType.Identifier,
               TokenType.Identifier, TokenType.Identifier, TokenType.Void, TokenType.EOF);
    TestTokens("1 .5 5m 'a' \"xx\" null true false", TokenType.Literal, TokenType.Literal, TokenType.Literal,
               TokenType.Literal, TokenType.Literal, TokenType.Literal, TokenType.Literal, TokenType.Literal);
    TestTokens("/** foo */ 1 /// <foo></foo>", TokenType.XmlComment, TokenType.Literal, TokenType.XmlComment);
    TestTokens("~ ! % ^ & | * ( ) - + { } [ ] : ; , . < > / ?", TokenType.Tilde, TokenType.Bang, TokenType.Percent,
               TokenType.Caret, TokenType.Ampersand, TokenType.Pipe, TokenType.Asterisk, TokenType.LParen,
               TokenType.RParen, TokenType.Minus, TokenType.Plus, TokenType.LCurly, TokenType.RCurly,
               TokenType.LSquare, TokenType.RSquare, TokenType.Colon, TokenType.Semicolon, TokenType.Comma,
               TokenType.Period, TokenType.LessThan, TokenType.GreaterThan, TokenType.Slash, TokenType.Question);
    TestTokens("&& || << >> <= >= == != :: ?? ++ -- ->", TokenType.LogAnd, TokenType.LogOr, TokenType.LShift,
               TokenType.RShift, TokenType.LessOrEq, TokenType.GreaterOrEq, TokenType.AreEqual, TokenType.NotEqual,
               TokenType.Scope, TokenType.NullCoalesce, TokenType.Increment, TokenType.Decrement, TokenType.Dereference);
    TestTokens("= %= ^= &= |= *= -= += /= <<= >>=", TokenType.OpAssign, TokenType.OpAssign, TokenType.OpAssign,
               TokenType.OpAssign, TokenType.OpAssign, TokenType.OpAssign, TokenType.OpAssign, TokenType.OpAssign,
               TokenType.OpAssign, TokenType.OpAssign, TokenType.OpAssign);
    TestValues("= %= ^= &= |= *= -= += /= <<= >>=", TokenType.Equals, TokenType.Percent, TokenType.Caret,
               TokenType.Ampersand, TokenType.Pipe, TokenType.Asterisk, TokenType.Minus, TokenType.Plus,
               TokenType.Slash, TokenType.LShift, TokenType.RShift);
  }

  static void TestTokens(string code, params TokenType[] values)
  {
    Compiler compiler = new Compiler();
    Scanner scanner = new Scanner(compiler, new StringReader(code));
    Token token;
    for(int i=0; i<values.Length; i++)
    {
      Assert.IsTrue(scanner.NextToken(out token));
      Assert.AreEqual(values[i], token.Type);
    }
    while(scanner.NextToken(out token)) { }
    Assert.AreEqual(0, compiler.Messages.Count);
  }
  #endregion

  #region TestValues
  [Test]
  public void TestValues()
  {
    // test identifiers
    TestValues(@"@void hel\u0041\U41o he\uFFFFFF a_今夜b", "void", "helAAo", "he\uFFFFFF", "a_今夜b");

    // test characters
    TestValues(@"'x' '\n' '\x41' '\0'", 'x', '\n', 'A', '\0');

    // test strings
    TestValues(@"""\'\""\\\a\f\r\v\t\b\n55blues\x5f\u1234""", "\'\"\\\a\f\r\v\t\b\n55blues\x5f\u1234");
    TestValues(@"@""hello""""there""", "hello\"there");
    TestValues(@"@'hello""there''boy'", "hello\"there'boy");
    
    // test integers
    TestValues("1 1u 1ul 1L", 1, 1u, 1ul, 1L);
    TestValues("0xffL 0xffffffff 0xfffffffff", (long)255, 0xffffffff, 0xfffffffff);
    TestValues("9223372036854775808", 9223372036854775808);

    // test floating point numbers
    TestValues("10e-5f 10.25f .5 7.2d", 10e-5f, 10.25f, .5, 7.2d);
    TestValues("10e2m 10.5m .5m", 10e2m, 10.5m, .5m);

    // literals
    TestValues("true false null", true, false, null);

    // doc comments
    TestValues("/// <foo></foo>", " <foo></foo>");
    TestValues("1 /* foo */ 2 /** foo foo * */", 1, 2, " foo foo * ");
  }

  static void TestValues(string code, params object[] values)
  {
    Compiler compiler = new Compiler();
    Scanner scanner = new Scanner(compiler, new StringReader(code));
    Token token;
    for(int i=0; i<values.Length; i++)
    {
      Assert.IsTrue(scanner.NextToken(out token));
      Assert.AreEqual(values[i], token.Data.Value);
      if(values[i] != null) Assert.AreSame(values[i].GetType(), token.Data.Value.GetType());
    }
    while(scanner.NextToken(out token)) { }
    Assert.AreEqual(0, compiler.Messages.Count);
  }
  #endregion
}

} // namespace Scripting.CSharper.Tests