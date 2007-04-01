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
    TestError(78, null, 1, 2, "1l");
    TestError(594, "Floating-point constant is outside the range of type 'double'", 1, 41,
              "class MyClass { void Main() { float f = 6.77777777777E400; } }");
    TestError(594, "Floating-point constant is outside the range of type 'float'", 1, 41,
              "class MyClass { void Main() { float f = 6.77777777777E100f; } }");
    TestError(1001, null, 1, 9, "#define ");
    TestError(1003, @"Expected character '\''", 2, 4, "  \n 'x\n'");
    TestError(1003, @"Expected character '\''", 1, 2, "'");
    TestError(1009, "Unrecognized escape sequence starting with 'q'", 1, 7, @"""\r\n\q\p""");
    TestError(1010, null, 1, 3, "\"x\n");
    TestError(1011, null, 1, 2, "''");
    TestError(1012, null, 1, 4, "'xa'");
    TestError(1013, null, 1, 2, " 0xzippy");
    TestError(1021, null, 1, 1, "123456789012345678901234567890");
    TestError(1024, null, 1, 2, "#");
    TestError(1024, null, 1, 1, "#foo");
    TestError(1025, null, 1, 13, "#define foo bar");
    TestError(1027, null, 2, 1, "#if DEBUG\n");
    TestError(1028, null, 1, 1, "#else");
    TestError(1028, null, 1, 1, "#endregion");
    TestError(1032, null, 2, 1, "5\n#define foo");
    TestError(1035, null, 1, 3, "/*");
    TestError(1038, null, 2, 1, "#region foo\n");
    TestError(1039, null, 1, 3, "\"x");
    TestError(1040, null, 1, 3, "5 #pragma");
    TestError(1056, @"Unexpected character '\b'", 1, 1, "\b");
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
    Scan(code, compiler);

    Assert.GreaterOrEqual(compiler.Messages.Count, 1);
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

  #region TestValues
  [Test]
  public void TestValues()
  {
    // test identifiers
    TestValue("@void", "void");
    TestValue(@"hel\u0041\U41o", "helAAo");
    TestValue(@"he\uFFFFFF", "he\uFFFFFF");

    // test characters
    TestValue(@"'x'", 'x');
    TestValue(@"'\n'", '\n');
    TestValue(@"'\x41'", 'A');

    // test strings
    TestValue(@"""\r\v\t\b\n55blues\x5f\u1234""", "\r\v\t\b\n55blues\x5f\u1234");
    TestValue(@"@""hello""""there""", "hello\"there");
    TestValue(@"@'hello""there''boy'", "hello\"there'boy");
    
    // test integers
    TestValue("1", 1);
    TestValue("1u", 1u);
    TestValue("1ul", 1L);
    TestValue("1L", 1ul);
    TestValue("0xffL", (long)255);
    TestValue("0xffffffff", 0xffffffff);
    TestValue("0xfffffffff", 0xfffffffff);
    TestValue("9223372036854775808", 9223372036854775808);

    // test floating point numbers
    TestValue("10e-5f", 10e-5f);
    TestValue("10e2m", 10e2m);
    TestValue("10.5m", 10.5m);
    TestValue("10.25f", 10.25f);
    TestValue(".5", .5);
    TestValue(".5m", .5m);
  }

  static void TestValue(string code, object value)
  {
    Compiler compiler = new Compiler();
    Scanner scanner = new Scanner(compiler, new StringReader(code));
    Token token;
    Assert.IsTrue(scanner.NextToken(out token));
    Assert.AreEqual(value, token.Data.Value);
    Assert.AreEqual(0, compiler.Messages.Count);
  }
  #endregion

  static Token[] Scan(string code, Compiler compiler)
  {
    Scanner scanner = new Scanner(compiler, new StringReader(code));
    List<Token> tokens = new List<Token>();
    Token token;
    while(scanner.NextToken(out token)) tokens.Add(token);
    return tokens.ToArray();
  }
}

} // namespace Scripting.CSharper.Tests