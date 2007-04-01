using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using AdamMil.Tests;
using Scripting.AST;

namespace Scripting.Tests
{

enum TokenType
{
  Invalid, Assign, Plus, Minus, Semicolon, Print, Int, Identifier, String, EOL, EOF
}

#region TestScanner
/// <summary>Implements a scanner for a simple language.</summary>
/// <remarks>The language is:
/// <code>
/// assign  ::=   =
/// plus    ::=   +
/// minus   ::=   -
/// semi    ::=   ;
/// print   ::=   print
/// int     ::=   \d+
/// ident   ::=   \w+
/// string  ::=   "(?:\\"|[^"])+"
/// eol     ::=   &lt;EOL&gt;
/// </code>
/// </remarks>
class TestScanner : ScannerBase<Compiler<CompilerOptions>,Token<TokenType,object>>
{
  public TestScanner(Compiler<CompilerOptions> compiler, Dictionary<string, string> sources)
    : base(compiler, CollectionHelpers.ToArray(sources.Keys))
  {
    this.sources = sources;
  }

  protected override System.IO.TextReader LoadSource(string name)
  {
    return new System.IO.StringReader(sources[name]);
  }

  protected override bool ReadToken(out Token<TokenType,object> token)
  {
    token = new Token<TokenType,object>();
    if(!EnsureValidSource()) return false;

    while(true)
    {
      char c = SkipWhitespace(false); // skip whitespace but not newlines
      token.Start = Position;

      if(char.IsLetter(c))
      {
        string ident = ReadIdent();
        token.Type = ident == "print" ? TokenType.Print : TokenType.Identifier;
        token.Data = ident;
      }
      else if(char.IsDigit(c))
      {
        token.Type = TokenType.Int;
        token.Data = int.Parse(ReadDigits());
      }
      else if(c == '\"')
      {
        token.Type = TokenType.String;
        token.Data = ReadString();
      }
      else
      {
        switch(c)
        {
          case '=':  token.Type = TokenType.Assign; break;
          case '+':  token.Type = TokenType.Plus; break;
          case '-':  token.Type = TokenType.Minus; break;
          case ';':  token.Type = TokenType.Semicolon; break;
          case '\n': token.Type = TokenType.EOL; break;
          case '\0':
            if(NextSource()) continue;
            else
            {
              token.Type = TokenType.EOF;
              return false;
            }
          default:
            AddErrorMessage("Unexpected character '"+c+"'");
            continue;
        }

        NextChar(); // advance to the next character
      }
      break;
    }

    token.End = LastPosition;
    return true;
  }

  string ReadDigits()
  {
    StringBuilder sb = new StringBuilder(16);
    do
    {
      sb.Append(Char);
    } while(char.IsDigit(NextChar()));
    return sb.ToString();
  }

  string ReadIdent()
  {
    StringBuilder sb = new StringBuilder(16);
    do
    {
      sb.Append(Char);
    } while(char.IsLetter(NextChar()));
    return sb.ToString();
  }

  string ReadString()
  {
    StringBuilder sb = new StringBuilder();
    while(true)
    {
      char c = NextChar();
      if(c == '\"')
      {
        NextChar(); // advance to the next character after the string
        break;
      }
      else if(c == '\\') c = NextChar();

      if(c == 0)
      {
        AddErrorMessage("Unterminated string constant.");
        break;
      }
      else
      {
        sb.Append(c);
      }
    }
    return sb.ToString();
  }

  readonly Dictionary<string, string> sources;
}
#endregion

#region ScannerTest
[TestFixture]
public class scannerTest
{
  [Test]
  public void Test()
  {
    string sourceA = "a = 5+2; b=4 ;\nprint a - b ",
           sourceB = "\r\nprint \"he said \\\"hello\\\",\ndontcha know.\"";
    Dictionary<string,string> sources = new Dictionary<string,string>();
    sources["A"] = sourceA;
    sources["B"] = sourceB;

    TestScanner scanner = new TestScanner(new Compiler<CompilerOptions>(), sources);
    
    AssertToken(scanner.NextToken(out token), TokenType.Identifier, "a",   1, 1, 1, 1);
    AssertToken(scanner.NextToken(out token), TokenType.Assign,     null,  1, 3, 1, 3);
    AssertToken(scanner.NextToken(out token), TokenType.Int,        5,     1, 5, 1, 5);
    AssertToken(scanner.NextToken(out token), TokenType.Plus,       null,  1, 6, 1, 6);
    AssertToken(scanner.NextToken(out token), TokenType.Int,        2,     1, 7, 1, 7);
    AssertToken(scanner.NextToken(out token), TokenType.Semicolon,  null,  1, 8, 1, 8);
    AssertToken(scanner.NextToken(out token), TokenType.Identifier, "b",   1, 10, 1, 10);
    AssertToken(scanner.NextToken(out token), TokenType.Assign,     null,  1, 11, 1, 11);
    AssertToken(scanner.NextToken(out token), TokenType.Int,        4,     1, 12, 1, 12);
    AssertToken(scanner.NextToken(out token), TokenType.Semicolon,  null,  1, 14, 1, 14);
    AssertToken(scanner.NextToken(out token), TokenType.EOL,        null,  1, 15, 1, 15);
    AssertToken(scanner.NextToken(out token), TokenType.Print,      null,  2, 1, 2, 5);
    AssertToken(scanner.NextToken(out token), TokenType.Identifier, "a",   2, 7, 2, 7);
    AssertToken(scanner.NextToken(out token), TokenType.Minus,      null,  2, 9, 2, 9);
    AssertToken(scanner.NextToken(out token), TokenType.Identifier, "b",   2, 11, 2, 11);
    AssertToken(scanner.NextToken(out token), TokenType.EOL,        null,  1, 1, 1, 1);
    AssertToken(scanner.NextToken(out token), TokenType.Print,      null,  2, 1, 2, 5);
    AssertToken(scanner.NextToken(out token), TokenType.String,     "he said \"hello\",\ndontcha know.", 2, 7, 3, 14);
    AssertToken(scanner.NextToken(out token), TokenType.Invalid, null, 0, 0, 0, 0);
  }

  void AssertToken(bool gotToken, TokenType type, object value,
                   int startLine, int startColumn, int endLine, int endColumn)
  {
    if(type == TokenType.Invalid)
    {
      Assert.IsFalse(gotToken);
    }
    else
    {
      Assert.IsTrue(gotToken);
      Assert.AreEqual(type, token.Type);
      if(value != null) Assert.AreEqual(value, token.Data);
      Assert.AreEqual(startLine, token.Start.Line);
      Assert.AreEqual(startColumn, token.Start.Column);
      Assert.AreEqual(endLine, token.End.Line);
      Assert.AreEqual(endColumn, token.End.Column);
    }
  }

  Token<TokenType,object> token;
}
#endregion

} // namespace Scripting.Tests