using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Scripting.AST;

namespace Scripting.CSharper
{

using Token = Scripting.AST.Token<TokenType,TokenData>;

#region TokenData
public struct TokenData
{
  public const int HiddenLine = -1, DefaultLine = -2;

  public object Value;
  public string SourceOverride;
  public int LineOverride;
}
#endregion

#region TokenType
  /// <summary>This enum represents the tokens that can form part of a C#r program.</summary>
public enum TokenType
{
  Invalid,
  /// <summary>A literal value, such as 42, true, "hello", or null. The literal will be stored in the token's value.</summary>
  Literal,
  /// <summary>The name of an identifier. Note that some identifiers are sometimes keywords, such as the "get" and
  /// "set" of property accessors.
  /// </summary>
  Identifier,
  /// <summary>A token signifying assignment. The token's value will be another TokenType that specifies the operator
  /// involved in the assignment (eg, Plus or LShift), or Equals if it was a straight assignment.
  /// </summary>
  Assign,
  /// <summary>A line from an XML comment.</summary>
  XmlCommentLine,
  /// <summary>The end of the current source file has been reached, but there may be more source files.</summary>
  EOF,
  /// <summary>The end of all source files have been reached.</summary>
  EOD,

  // punctuation
  Tilde, Bang, Percent, Caret, Ampersand, Pipe, Asterisk, LParen, RParen, Minus, Plus, Equals,
  LCurly, RCurly, LSquare, RSquare, Colon, Semicolon, Comma, Period, LessThan, GreaterThan, Slash, Question,

  // punctuation combinations
  LogAnd, LogOr, LShift, RShift, LessOrEq, GreaterOrEq, AreEqual, NotEqual, Scope, NullCoalesce, Increment, Decrement,

  // keywords
  KeywordStart,
  Abstract=KeywordStart, As, Base, Bool, Break, Byte, Case, Catch, Char, Checked, Class, Const, Continue, Decimal,
  Default, Delegate, Do, Double, Else, Enum, Event, Explicit, Extern, False, Finally, Fixed, Float, For, ForEach,
  Goto, If, Implicit, In, Int, Interface, Internal, Is, Lock, Long, Namespace, New, Null, Object, Operator, Out,
  Override, Params, Private, Protected, Public, Readonly, Ref, Return, Sbyte, Sealed, Short, SizeOf, StackAlloc,
  Static, String, Struct, Switch, This, Throw, True, Try, TypeOf, Uint, Ulong, Unchecked, Unsafe, Ushort, Using,
  Virtual, Void, Volatile, While, KeywordEnd
}
#endregion

#region Scanner
public class Scanner : ScannerBase<Compiler,Token>
{
  public Scanner(Compiler compiler, params string[] sourceNames) : base(compiler, sourceNames) { }
  public Scanner(Compiler compiler, params System.IO.TextReader[] sources) : base(compiler, sources) { }
  public Scanner(Compiler compiler, System.IO.TextReader[] sources, string[] sourceNames)
    : base(compiler, sources, sourceNames) { }

  static Scanner()
  {
    // initialize the keyword map
    for(TokenType keyToken=TokenType.KeywordStart; keyToken<TokenType.KeywordEnd; keyToken++)
    {
      keywords[Enum.GetName(typeof(TokenType), keyToken).ToLowerInvariant()] = keyToken;
    }
  }

  /// <summary>Reset source-specific state and push a new set of options onto the option stack.</summary>
  protected override void OnSourceLoaded()
  {
    // reset file-specific state
    ppNesting.Clear();
    regionDepth    = 0;
    firstOnLine    = true;
    sawNonPP       = false;
    lineOverride   = TokenData.DefaultLine;
    sourceOverride = null;

    Compiler.PushOptions();
  }

  protected override bool ReadToken(out Token token)
  {
    token = new Token();

    if(!EnsureValidSource()) // if we're done with all of the source files, return false
    {
      token.Type = TokenType.EOD;
      return false;
    }

    while(true)
    {
      char c = SkipWhitespace();

      if(c == 0) // we're at the end of this file
      {
        if(ppNesting.Count != 0) AddMessage(Diagnostic.PPEndIfExpected);
        if(regionDepth != 0) AddMessage(Diagnostic.EndRegionExpected);
        Compiler.PopOptions(); // pop the options we pushed in LoadSource
        NextSource(); // move to the next file (if any)
        token.Type = TokenType.EOF;
        return true;
      }

      token.SourceName = SourceName;
      token.Start      = Position;

      #region Period or Numeric literal
      if(char.IsDigit(c) || c == '.')
      {
        if(c == '.')
        {
          if(!char.IsDigit(NextChar())) // Period
          {
            token.Type = TokenType.Period;
            break; // return it
          }
        }
        else NextChar();

        token.Type = TokenType.Literal;

        bool isInteger;
        ulong integerValue = 0;
        
        // if it started with zero, it might be a hex number 0x...
        bool isHex = c == '0' && char.ToLowerInvariant(Char) == 'x';
        if(isHex) // if it's a hex number, skip the leading 0x
        {
          uint digitValue;

          isInteger = true;
          if(!IsHexDigit(NextChar(), out digitValue))
          {
            AddMessage(Diagnostic.InvalidNumber, token.Start);
          }
          else
          {
            integerValue = digitValue;
            while(IsHexDigit(NextChar(), out digitValue))
            {
              ulong newValue = (integerValue << 4) + digitValue;
              if(newValue < integerValue) // there's been an overflow
              {
                AddMessage(Diagnostic.IntegralConstantTooLarge, token.Start);
                while(IsHexDigit(NextChar(), out digitValue)) { } // skip remaining digits
                integerValue = 0;
                break;
              }
              integerValue = newValue;
            }
          }
        }
        else // otherwise, it's either an integer or a real
        {
          StringBuilder sb = new StringBuilder(16);
          sb.Append(c);
          isInteger = c != '.'; // whether a period has been seen yet

          int sawExponent = -1;
          while(true)
          {
            c = char.ToLowerInvariant(Char);
            
            // break if it's not a digit, and it's not 'e' (or it is 'e', but we've already got one), and it's not '.'
            // (or it is '.' but we've already got one), and it's not '-' (or it is '-', but it's not right after 'e')
            if(!char.IsDigit(c) && (c != 'e' || sawExponent != -1) && (c != '.' || !isInteger) &&
               (c != '-' || sb.Length != sawExponent))
            {
              break;
            }

            if(c == 'e')
            {
              sawExponent = sb.Length+1;
              isInteger   = false;
            }
            else if(c == '.')
            {
              isInteger = false;
            }

            sb.Append(c);
            NextChar();
          }

          Type realType = null;
          try
          {
            string numberString = sb.ToString();

            if(isInteger)
            {
              integerValue = ulong.Parse(numberString, CultureInfo.InvariantCulture);
            }
            else if(char.ToLowerInvariant(Char) == 'm') // a decimal value
            {
              realType = typeof(decimal);
              NextChar(); // skip past the suffix
              token.Data.Value = sawExponent == -1 ? decimal.Parse(numberString, CultureInfo.InvariantCulture)
                                   : new decimal(double.Parse(numberString, CultureInfo.InvariantCulture));
              break; // return the token
            }
            else
            {
              realType = typeof(double);
              double realValue = double.Parse(numberString, CultureInfo.InvariantCulture);

              if(char.ToLowerInvariant(Char) == 'f') // a 'float' suffix
              {
                NextChar();

                float floatValue = (float)realValue;
                if(float.IsInfinity(floatValue))
                {
                  AddMessage(Diagnostic.RealConstantTooLarge, token.Start, Diagnostic.TypeName(typeof(float)));
                }
                token.Data.Value = floatValue;
              }
              else
              {
                if(char.ToLowerInvariant(Char) == 'd') NextChar(); // a 'double' suffix
                token.Data.Value = realValue;
              }
              break; // return the token
            }
          }
          catch(FormatException)
          {
            AddMessage(Diagnostic.InvalidNumber, token.Start);
          }
          catch(OverflowException)
          {
            if(isInteger)
            {
              AddMessage(Diagnostic.IntegralConstantTooLarge, token.Start);
              token.Data.Value = 0;
            }
            else
            {
              AddMessage(Diagnostic.RealConstantTooLarge, token.Start, Diagnostic.TypeName(realType));
              token.Data.Value = 0.0;
            }
            break; // return the token
          }
        }

        Debug.Assert(isInteger); // if we got here, it should be an integer
        
        bool unsigned = false, longFlag = false;

        c = char.ToLowerInvariant(Char);
        if(c == 'u') unsigned = true;
        else if(c == 'l')
        {
          if(Char == 'l') AddMessage(Diagnostic.UseUppercaseL);
          longFlag = true;
        }

        if(unsigned || longFlag) // if we got a flag, skip past it and check for another flag
        {
          NextChar();
          if(unsigned && char.ToLowerInvariant(Char) == 'l') longFlag = true;
          else if(longFlag && char.ToLowerInvariant(Char) == 'u') unsigned = true;
        }

        if(unsigned && longFlag) NextChar(); // if we got a second flag, skip it as well

        if(integerValue > (ulong)long.MaxValue)
        {
          token.Data.Value = integerValue;
        }
        else if(integerValue > uint.MaxValue || longFlag)
        {
          token.Data.Value = unsigned ? (object)integerValue : (long)integerValue;
        }
        else if(integerValue > int.MaxValue || unsigned)
        {
          token.Data.Value = (uint)integerValue;
        }
        else
        {
          token.Data.Value = (int)integerValue;
        }

        break; // return the token
      }
      #endregion

      #region Identifier, keyword, or verbatim string
      else if(char.IsLetter(c) || c == '@' || c == '_' || c == '\\')
      {
        bool verbatim = c == '@';
        if(verbatim)
        {
          c = NextChar();
          if(!char.IsLetter(c) && c != '_' && c != '\\' && c != '"' && c != '\'') // we expect an identifier or string after @
          {
            AddMessage(Diagnostic.MisplacedVerbatim);
            goto continueScanning;
          }
        }
        
        if(c == '"' || c == '\'') // verbatim string
        {
          StringBuilder sb = new StringBuilder();
          char delim = c;
          while(true)
          {
            c = NextChar();
            if(c == delim) // if we encounter the delimiter again, end the string unless the delimiter is doubled
            {
              if(NextChar() == delim) sb.Append(delim);
              else break;
            }
            else if(c == 0)
            {
              AddMessage(Diagnostic.UnterminatedStringLiteral);
              break;
            }
            else
            {
              sb.Append(c);
            }
          }

          token.Type = TokenType.Literal;
          token.Data.Value = sb.ToString();
        }
        else // identifier
        {
          string identifier = ReadIdentifier();
          if(identifier == null) goto continueScanning;

          if(!verbatim || keywords.TryGetValue(identifier, out token.Type)) // if it's not a keyword, it's an identifier
          {
            token.Type = TokenType.Identifier;
            token.Data.Value = identifier;
          }
          // if it's a true, false, or null keyword, represent it as a literal token
          else if(token.Type == TokenType.True || token.Type == TokenType.False || token.Type == TokenType.Null)
          {
            token.Type = TokenType.Literal;
            token.Data.Value = token.Type == TokenType.True ? true : token.Type == TokenType.False ? (object)false : null;
          }
        }
        break;
      }
      #endregion

      #region Character literal
      else if(c == '\'')
      {
        c = NextChar();
        if(c == '\'') AddMessage(Diagnostic.EmptyCharacterLiteral);
        else if(c == '\n') AddMessage(Diagnostic.NewlineInConstant);
        else
        {
          if(c == '\\') c = ProcessEscape(true);
          else NextChar(); // skip after the character

          if(Char != '\'') // if it's not followed immediately by a closing quote, find the closing quote and complain
          {
            FilePosition expectedAt = Position;
            while(Char != '\'' && Char != '\n' && Char != 0) NextChar();
            if(Char == '\'') AddMessage(Diagnostic.CharacterLiteralTooLong);
            else AddMessage(Diagnostic.ExpectedCharacter, expectedAt, Diagnostic.CharLiteral('\''));
          }
          NextChar(); // skip past the closing quote
        }
 
        token.Type = TokenType.Literal;
        token.Data.Value = c;

        break; // return the token
      }
      #endregion

      #region String literal
      else if(c == '"')
      {
        StringBuilder sb = new StringBuilder();
        c = NextChar();
        while(true)
        {
          if(c == 0)
          {
            AddMessage(Diagnostic.UnterminatedStringLiteral);
            break;
          }
          else if(c == '\n')
          {
            AddMessage(Diagnostic.NewlineInConstant);
            break;
          }
          else if(c == '\\')
          {
            sb.Append(ProcessEscape(true));
            c = Char;
          }
          else if(c == '"')
          {
            NextChar(); // skip the closing quote
            break;
          }
          else
          {
            sb.Append(c);
            c = NextChar();
          }
        }
        token.Type = TokenType.Literal;
        token.Data.Value = sb.ToString();
        break; // return the token
      }
      #endregion

      #region Preprocessor directives
      else if(c == '#')
      {
        if(!firstOnLine)
        {
          AddMessage(Diagnostic.PPNotFirstToken);
          SkipToEOL();
          goto continueScanning;
        }

        NextChar(); // skip over the '#'
        if(SkipWhitespace(false) == '\n' || Char == 0) // skip whitespace after the '#'
        {
          AddMessage(Diagnostic.PPDirectiveExpected);
          goto continueScanning;
        }

        StringBuilder sb = new StringBuilder(9);
        do
        {
          sb.Append(Char);
        } while(!char.IsWhiteSpace(NextChar()) && Char != 0);

        SkipWhitespace(false);
        switch(sb.ToString())
        {
          case "line":
          {
            string line = ReadRestOfLine(true);

            Match m = lineRe.Match(line);
            if(!m.Success)
            {
              AddMessage(Diagnostic.InvalidLineDirective, token.Start);
            }
            else
            {
              // default to no override
              lineOverride   = TokenData.DefaultLine;
              sourceOverride = null;
              if(m.Groups[1].Value == "hidden")
              {
                lineOverride = TokenData.HiddenLine;
              }
              else if(m.Groups[1].Value != "default")
              {
                try
                {
                  lineOverride   = int.Parse(m.Groups[1].Value);
                  sourceOverride = m.Groups[2].Value.Trim();
                  if(sourceOverride.Length == 0) sourceOverride = null;
                }
                catch(OverflowException)
                {
                  AddMessage(Diagnostic.IntegralConstantTooLarge, token.Start);
                }
              }
            }

            goto continueScanning;
          }

          case "if": case "elif":
          {
            if(sb[0] == 'e')
            {
              if(ppNesting.Count == 0) AddMessage(Diagnostic.UnexpectedPPDirective, token.Start);
              else if(ppNesting.Peek()) // if we already found a true value, skip this block
              {
                PPSkip(false);
                goto continueScanning;
              }
            }

            bool conditional;
            if(!PPEvaluate(ReadRestOfLine(true), out conditional))
            {
              AddMessage(Diagnostic.InvalidPPExpression, token.Start);
            }
            
            if(sb[0] == 'e') ppNesting.Pop();
            ppNesting.Push(conditional);

            if(!conditional) PPSkip(false);
            goto continueScanning;
          }

          case "else":
            if(ppNesting.Count == 0) AddMessage(Diagnostic.UnexpectedPPDirective, token.Start);
            else if(!ppNesting.Peek()) PPSkip(true);
            else FinishPPLine();
            goto continueScanning;

          case "endif":
            if(ppNesting.Count == 0) AddMessage(Diagnostic.UnexpectedPPDirective, token.Start);
            ppNesting.Pop();
            FinishPPLine();
            goto continueScanning;

          case "region":
            regionDepth++;
            SkipToEOL();
            goto continueScanning;

          case "endregion":
            if(regionDepth == 0) AddMessage(Diagnostic.UnexpectedPPDirective, token.Start);
            else regionDepth--;
            SkipToEOL();
            goto continueScanning;

          case "pragma":
          {
            string line = ReadRestOfLine(false);

            Match m = warningRe.Match(line);
            if(m.Success)
            {
              bool disable = m.Groups[1].Value == "disable";

              // parse the list of warning numbers
              string[] warningStrings = m.Groups[2].Value.Split(',');
              for(int i=0; i<warningStrings.Length; i++)
              {
                int warningCode;
                if(!int.TryParse(warningStrings[i], out warningCode) || !Diagnostic.IsValidWarning(warningCode))
                {
                  AddMessage(Diagnostic.InvalidWarningCode, token.Start, warningStrings[i].Trim());
                }
                else if(disable) Compiler.Options.DisableWarning(warningCode);
                else Compiler.Options.RestoreWarning(warningCode);
              }
            }
            else if(line.StartsWith("warning", StringComparison.InvariantCultureIgnoreCase))
            {
              AddMessage(Diagnostic.InvalidWarningPragma, token.Start);
            }
            else
            {
              AddMessage(Diagnostic.UnrecognizedPragma, token.Start);
            }

            goto continueScanning;
          }

          case "define": case "undef":
          {
            if(sawNonPP) // #define and #undef need to come before any non-preprocessor lines in the file
            {
              AddMessage(Diagnostic.PPTooLate, token.Start);
              SkipToEOL();
              goto continueScanning;
            }

            string name = ReadIdentifier();
            if(name == null)
            {
              AddMessage(Diagnostic.ExpectedIdentifier);
            }
            else
            {
              if(sb[0] == 'd') Compiler.Options.Define(name);
              else Compiler.Options.Undefine(name);
            }
            FinishPPLine();
            goto continueScanning;
          }

          case "warning": case "error":
          {
            OutputMessageType type = sb[0] == 'w' ? OutputMessageType.Warning : OutputMessageType.Error;
            string prefix = type == OutputMessageType.Warning ? "warning: " : "error: ";
            AddMessage(new OutputMessage(type, prefix+ReadRestOfLine(false), SourceName, token.Start));
            goto continueScanning;
          }

          default:
            AddMessage(Diagnostic.PPDirectiveExpected, token.Start);
            SkipToEOL();
            goto continueScanning;
        }
      }
      #endregion

      #region Punctuation, etc
      else
      {
        bool checkAssign = false, skipChar = true;
        switch(c)
        {
          case '~': token.Type = TokenType.Tilde; break;
          case '%': token.Type = TokenType.Percent; checkAssign=true; break;
          case '^': token.Type = TokenType.Caret; checkAssign=true; break;
          case '*': token.Type = TokenType.Asterisk; checkAssign=true; break;
          case '(': token.Type = TokenType.LParen; break;
          case ')': token.Type = TokenType.RParen; break;
          case '-': token.Type = TokenType.Minus; checkAssign=true; break;
          case '+': token.Type = TokenType.Plus; checkAssign=true; break;
          case '{': token.Type = TokenType.LCurly; break;
          case '}': token.Type = TokenType.RCurly; break;
          case '[': token.Type = TokenType.LSquare; break;
          case ']': token.Type = TokenType.RSquare; break;
          case ';': token.Type = TokenType.Semicolon; break;
          case ',': token.Type = TokenType.Comma; break;
          case '.': token.Type = TokenType.Period; break; // this should be handled by the numeric code

          case '=':
            if(NextChar() == '=') token.Type = TokenType.AreEqual;
            else { token.Type = TokenType.Assign; token.Data.Value = TokenType.Equals; skipChar=false; }
            break;
          case '!':
            if(NextChar() == '=') token.Type = TokenType.NotEqual;
            else { token.Type = TokenType.Bang; skipChar=false; }
            break;
          case '&':
            if(NextChar() == '&') token.Type = TokenType.LogAnd;
            else { token.Type = TokenType.Ampersand; checkAssign=true; skipChar=false; }
            break;
          case '|':
            if(NextChar() == '|') token.Type = TokenType.LogOr;
            else { token.Type = TokenType.Pipe; checkAssign=true; skipChar=false; }
            break;
          case ':':
            if(NextChar() == ':') token.Type = TokenType.Scope;
            else { token.Type = TokenType.Colon; skipChar=false; }
            break;
          case '<':
            if(NextChar() == '=') token.Type = TokenType.LessOrEq;
            else { token.Type = TokenType.LessThan; skipChar=false; }
            break;
          case '>':
            if(NextChar() == '=') token.Type = TokenType.GreaterOrEq;
            else { token.Type = TokenType.GreaterThan; skipChar=false; }
            break;
          case '?':
            if(NextChar() == '?') token.Type = TokenType.NullCoalesce;
            else { token.Type = TokenType.Question; skipChar=false; }
            break;

          case '/':
            if(NextChar() == '/') // single-line comment
            {
              if(NextChar() != '/') // not a triple-slash comment
              {
                SkipToEOL();
                goto continueScanning;
              }
              else // triple-slash comment -- return the rest as an XML doc line
              {
                NextChar(); // skip the final slash
                token.Type  = TokenType.XmlCommentLine;
                token.Data.Value = ReadRestOfLine(false);
                break;
              }
            }
            else if(Char == '*') // multi-line comment
            {
              NextChar();
              while(true)
              {
                if(Char == '*')
                {
                  if(NextChar() == '/') break;
                }
                else if(Char == 0)
                {
                  AddMessage(Diagnostic.UnterminatedComment);
                  break;
                }
                else NextChar();
              }
              goto continueScanning;
            }
            else
            {
              token.Type = TokenType.Slash;
              checkAssign = true;
              skipChar = false;
            }
            break;

          default:
            AddMessage(Diagnostic.UnexpectedCharacter, token.Start, Diagnostic.CharLiteral(c));
            break;
        }

        if(skipChar) NextChar();

        if(checkAssign && Char == '=')
        {
          token.Data.Value = token.Type;
          token.Type = TokenType.Assign;
          NextChar();
        }

        break; // return this token
      }
      #endregion

      continueScanning:
      firstOnLine = Char == '\n';
      sawNonPP    = true;
    }

    firstOnLine = Char == '\n';
    sawNonPP    = true;

    token.End = LastPosition;
    token.Data.LineOverride   = lineOverride;
    token.Data.SourceOverride = sourceOverride;
    
    return true;
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic)
  {
    AddMessage(diagnostic, Position, new object[0]);
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic, FilePosition position)
  {
    AddMessage(diagnostic, position, new object[0]);
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic, FilePosition position, params object[] args)
  {
    if(diagnostic.Type != OutputMessageType.Warning || !Compiler.Options.IsWarningDisabled(diagnostic.Code))
    {
      AddMessage(diagnostic.ToMessage(Compiler.Options.TreatWarningsAsErrors, SourceName, position, args));
    }
  }

  /// <summary>Ensures that the rest of the line is a valid finish for a preprocessor line.</summary>
  /// <remarks>A preprocessor line can end with a single-line comment or a newline.</remarks>
  void FinishPPLine()
  {
    if(SkipWhitespace(false) != '\n')
    {
      if(Char != '/' || NextChar() != '/') AddMessage(Diagnostic.PPEndExpected);
    }
  }

  /// <summary>Returns the next character, setting <see cref="firstOnLine"/> to true if a newline was encountered.</summary>
  new char NextChar()
  {
    char c = base.NextChar();
    if(c == '\n') firstOnLine = true;
    return c;
  }

  /// <summary>Evaluates the given preprocessor expression.</summary>
  /// <param name="value">A variable that receives whether the expression is true or false.</param>
  /// <returns>True if the expression was well-formed and false otherwise.</returns>
  bool PPEvaluate(string expression, out bool value)
  {
    value = false;

    List<string> tokens = new List<string>();

    // gather all the tokens in the expression, making sure that each token occurs immediately after the previous and
    // that the entire string is consumed in the process. otherwise, the expression string is invalid.
    int lastMatch = 0;
    Match m = ppExprRe.Match(expression);
    while(m.Success)
    {
      if(m.Index != lastMatch) return false; // if this match wasn't immediately after the previous, it's invalid

      string token = m.Value;
      if(token.IndexOf('\\') != -1) // if the token contains an escape sequence (\u....), unescape it
      {
        token = ppIdentEscapeRe.Replace(token, PPIdentUnescape);
      }
      tokens.Add(token);
      
      lastMatch = m.Index+m.Length;
      m = m.NextMatch();
    }
    if(lastMatch != expression.Length) return false; // make sure the entire string was consumed

    try
    {
      int index = 0;
      value = PPEvaluateOr(tokens, ref index);
      return true;
    }
    catch(FormatException)
    {
      return false;
    }
  }

  bool PPEvaluateOr(List<string> tokens, ref int index)
  {
    bool value = PPEvaluateAnd(tokens, ref index);
    while(index < tokens.Count)
    {
      if(tokens[index++] != "||") throw new FormatException();
      value = PPEvaluateAnd(tokens, ref index) || value;
    }
    return value;
  }

  bool PPEvaluateAnd(List<string> tokens, ref int index)
  {
    bool value = PPEvaluateEquality(tokens, ref index);
    while(index < tokens.Count)
    {
      if(tokens[index++] != "&&") throw new FormatException();
      value = PPEvaluateEquality(tokens, ref index) && value;
    }
    return value;
  }

  bool PPEvaluateEquality(List<string> tokens, ref int index)
  {
    bool value = PPEvaluateUnary(tokens, ref index);
    while(index < tokens.Count)
    {
      bool not;
      if(tokens[index] == "==") not = false;
      else if(tokens[index] == "!=") not = true;
      else throw new FormatException();
      index++;

      bool rhs = PPEvaluateUnary(tokens, ref index);
      value = not ? value == rhs : value != rhs;
    }
    return value;
  }

  bool PPEvaluateUnary(List<string> tokens, ref int index)
  {
    bool not = false;
    while(index < tokens.Count && tokens[index] == "!")
    {
      index++;
      not = !not;
    }
    bool value = PPEvaluatePrimary(tokens, ref index);
    return not ? !value : value;
  }

  bool PPEvaluatePrimary(List<string> tokens, ref int index)
  {
    if(index == tokens.Count) throw new FormatException();

    bool value;
    if(tokens[index] == "true") value = true;
    else if(tokens[index] == "false") value = false;
    else if(tokens[index] == "(")
    {
      index++;
      value = PPEvaluateOr(tokens, ref index);
      if(index == tokens.Count || tokens[index] != ")") throw new FormatException();
    }
    else value = Compiler.Options.IsDefined(tokens[index]);

    index++;
    return value;
  }

  static string PPIdentUnescape(Match m)
  {
    string escape = m.Value;
    uint value = 0;
    for(int i=2; i<escape.Length; i++)
    {
      uint digitValue;
      IsHexDigit(escape[i], out digitValue);
      value = (value<<4) + digitValue;
    }
    return new string((char)value, 1);
  }

  /// <summary>Skips the current #if/#elif/#else block.</summary>
  void PPSkip(bool skippingElse)
  {
    int depth = 0; // we need to process nested #if blocks in the current block as well

    while(true)
    {
      SkipToEOL();  // skip to the end of this line
      NextChar();   // then move to the next line
      if(SkipWhitespace(false) == '#') // if this line starts with a PP directive, process it
      {
        if(depth == 0) SaveState(); // if this might be where we stop, save the state.
        
        NextChar();
        string ident = ReadPPIdentifier();

        if(ident == "if") // if it's #if, we need to increase our depth
        {
          depth++;
        }
        else if(ident == "endif") // if it's #endif, we're done if depth == 0, otherwise decrease our depth
        {
          if(depth == 0)
          {
            RestoreState();
            break;
          }
          else depth--;
        }
        // with else and elif, our depth remains unchanged, so we just exit if depth == 0 (and !skippingElse)
        else if(depth == 0 && (ident == "else" || ident == "elif"))
        {
          RestoreState(); // restore state in any case, so we can report errors properly if necessary
          if(skippingElse) AddMessage(Diagnostic.UnexpectedPPDirective); // we shouldn't find #elif/#else after #else
          else break; // we're done!
        }
      }
      else if(Char == 0) // if we hit EOF, abort
      {
        AddMessage(Diagnostic.PPEndIfExpected);
        break;
      }
    }
  }

  /// <summary>Processes the escape code at the current position and returns the corresponding character.</summary>
  /// <param name="skipAChar">If true, skips a character (the escape character) before doing anything.</param>
  char ProcessEscape(bool skipAChar)
  {
    if(skipAChar) NextChar();

    char c;
    switch(Char)
    {
      case '\'': c = '\''; break;
      case '"':  c = '"'; break;
      case '\\': c = '\\'; break;
      case '0':  c = '\0'; break;
      case 'a':  c = '\a'; break;
      case 'b':  c = '\b'; break;
      case 'f':  c = '\f'; break;
      case 'n':  c = '\n'; break;
      case 'r':  c = '\r'; break;
      case 't':  c = '\t'; break;
      case 'v':  c = '\v'; break;

      case 'x': case 'u': case 'U': // unicode character specified by up to 4 hex digits (\u1234)
      {
        uint value = 0;
        int i;
        for(i=0; i<4; i++)
        {
          uint digitValue;
          if(!IsHexDigit(NextChar(), out digitValue))
          {
            if(i == 0) goto default; // there must be at least one hex digit
            break;
          }
          value = (value<<4) + digitValue;
        }
        if(i == 4) NextChar(); // make sure we skip past all hex digits
        return (char)value;
      }

      default:
        AddMessage(Diagnostic.UnrecognizedEscape, Position, Diagnostic.CharLiteral(Char));
        c = 'X'; // return an arbitrary character
        break;
    }

    NextChar();
    return c;
  }

  /// <summary>Reads an identifier, assusing the scanner is positioned at one.</summary>
  string ReadIdentifier()
  {
    char c = Char;
    if(!char.IsLetter(c) && c != '_' && c != '\\') return null;

    FilePosition start = Position;
    StringBuilder sb = new StringBuilder();

    do
    {
      if(c == '\\')
      {
        if(char.ToLowerInvariant(NextChar()) != 'u')
        {
          if(sb.Length == 0) // if we see a backslash all by itself, it's an error
          {
            AddMessage(Diagnostic.UnexpectedCharacter, start, Diagnostic.CharLiteral(Char));
            return null;
          }
          break; // but if we have part of an identifier in 'sb', we'll return it first
        }
        sb.Append(ProcessEscape(false));
        c = Char;
      }
      else
      {
        sb.Append(c);
        c = NextChar();
      }
    } while(char.IsLetterOrDigit(c) || c == '_' || c == '\\');

    return sb.ToString();
  }

  /// <summary>Skips whitespace (not including newlines) and then reads an identifier.</summary>
  string ReadPPIdentifier()
  {
    SkipWhitespace(false);
    return ReadIdentifier();
  }

  /// <summary>Reads and returns the rest of the line, excluding the newline character.</summary>
  string ReadRestOfLine(bool excludeSingleLineComment)
  {
    StringBuilder sb = new StringBuilder();
    if(excludeSingleLineComment)
    {
      while(Char != '\n' && Char != 0)
      {
        if(Char == '/')
        {
          if(NextChar() == '/')
          {
            SkipToEOL();
            break;
          }
          else sb.Append('/');
        }
        sb.Append(Char);
        NextChar();
      }
    }
    else
    {
      while(Char != '\n' && Char != 0)
      {
        sb.Append(Char);
        NextChar();
      }
    }
    return sb.ToString();
  }

  /// <summary>Skips the rest of the line, excluding the newline character.</summary>
  void SkipToEOL()
  {
    while(Char != '\n' && Char != 0) NextChar(); // skip the rest of the line
  }

  /// <summary>Skips whitespace, setting <see cref="firstOnLine"/> to true if a newline was encountered.</summary>
  new char SkipWhitespace()
  {
    char c = base.SkipWhitespace(false);
    if(c == '\n')
    {
      firstOnLine = true;
      c = base.SkipWhitespace(true);
    }
    return c;
  }

  /// <summary>Overrides the current source name with this, if it's not equal to null.</summary>
  string sourceOverride;
  /// <summary>The values of evaluating preprocessor #if/#elif tokens.</summary>
  Stack<bool> ppNesting = new Stack<bool>();
  /// <summary>The depth of nested #regions.</summary>
  int regionDepth;
  /// <summary>Overrides the current line with this, if it's not equal to <see cref="DefaultLine"/>.</summary>
  int lineOverride;
  /// <summary>Whether this is the first token on the current line.</summary>
  bool firstOnLine;
  /// <summary>Whether this file has had any non-preprocessor tokens yet.</summary>
  bool sawNonPP;

  /// <summary>Determines whether the given character is a valid hex digit.</summary>
  static bool IsHexDigit(char c, out uint digitValue)
  {
    if(char.IsDigit(c))
    {
      digitValue = (uint)(c-'0');
      return true;
    }
    else
    {
      c = char.ToLowerInvariant(c);
      digitValue = (uint)(10 + (c-'a'));
      return c >= 'a' && c <= 'f';
    }
  }

  /// <summary>A map of strings to keyword tokens.</summary>
  static readonly Dictionary<string, TokenType> keywords =
    new Dictionary<string, TokenType>(TokenType.KeywordEnd-TokenType.KeywordStart);

  static readonly Regex lineRe = new Regex(@"^(hidden|default|\d+(?:\s*""([^""]+)"")?)\s*$",
                                           RegexOptions.Compiled | RegexOptions.Singleline);
  static readonly Regex ppExprRe =
    new Regex(@"\s*(\|\||&&|==|!=|!|true|false|\w(?:[\w\d]|\\[uU][0-9a-fA-F]{1,4})+)\s*",
              RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

  static readonly Regex ppIdentEscapeRe = new Regex(@"\\[uU]....", RegexOptions.Singleline);

  static readonly Regex warningRe = new Regex(@"^warning\s+(disable|restore)\s+(\d+(?:\s*,\s*\d+)*)\s*$",
                                              RegexOptions.CultureInvariant | RegexOptions.Singleline);
}
#endregion

} // namespace Scripting.CSharper