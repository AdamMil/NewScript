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
/// <summary>This struct contains extra associated with each token.</summary>
public struct TokenData
{
  /// <summary>A constant that indicates that the line should be hidden from the debugger.</summary>
  public const int HiddenLine = -1;
  /// <summary>A constant that indicates that the default line should be used.</summary>
  public const int DefaultLine = -2;

  /// <summary>The value associated with this token. For identifiers, it's the identifier name. For literals, it's the
  /// literal value. Etc.
  /// </summary>
  public object Value;
  /// <summary>A name of a file that will override the default source name in debugger symbols.</summary>
  public string SourceOverride;
  /// <summary>A line number that will override the default line number in debugger symbols.</summary>
  public int LineOverride;
}
#endregion

#region TokenType
/// <summary>This enum represents the tokens that can form part of a C#er program.</summary>
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
/// <summary>This class will convert C#er source code into a stream of tokens.</summary>
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

    if(!EnsureValidSource()) // if we're done with all of the source files, return false -- there are no more tokens
    {
      token.Type = TokenType.EOD;
      return false;
    }

    while(true) // while we haven't found a token yet
    {
      char c = SkipWhitespace(); // skip to a non-whitespace character

      if(c == 0) // if we're at the end of the file
      {
        // check that we don't have any open preprocessor directives (eg, #region or #if)
        if(ppNesting.Count != 0) AddMessage(Diagnostic.PPEndIfExpected);
        if(regionDepth != 0) AddMessage(Diagnostic.EndRegionExpected);

        Compiler.PopOptions(); // pop the options we pushed in LoadSource
        NextSource(); // move to the next source file (if any)
        token.Type = TokenType.EOF; // return an EOF token
        return true;
      }

      token.SourceName = SourceName;
      token.Start      = Position;

      #region Identifier, keyword, or verbatim string
      if(IsIdentifierStart(c) || c == '@' || c == '\\')
      {
        bool verbatim = c == '@';
        if(verbatim) // make sure a verbatim symbol is followed immediately by an appropriate character
        {
          c = NextChar();
          if(!IsIdentifierStart(c) && c != '\\' && c != '"' && c != '\'') // we expect an identifier or string after @
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
        else // identifier (possibly verbatim)
        {
          bool hadEscape;
          string identifier = ReadIdentifier(out hadEscape);
          if(identifier == null) goto continueScanning;

          if(hadEscape || verbatim || // verbatim identifiers and those with escape codes cannot be keywords
             !keywords.TryGetValue(identifier, out token.Type))
          {
            token.Type = TokenType.Identifier;
            token.Data.Value = identifier;
          }
          // if it's a true, false, or null keyword, represent it as a literal token
          else if(token.Type == TokenType.True || token.Type == TokenType.False || token.Type == TokenType.Null)
          {
            token.Data.Value = token.Type == TokenType.True ? true : token.Type == TokenType.False ? (object)false : null;
            token.Type = TokenType.Literal;
          }
        }
        break;
      }
      #endregion

      #region Period or Numeric literal
      else if(c == '.' || char.IsDigit(c))
      {
        NextChar();

        if(c == '.') // if it starts with a period, see if it's just a period
        {
          if(!char.IsDigit(Char)) // if no digits follow, it's just a period
          {
            token.Type = TokenType.Period;
            break; // return it
          }
        }

        // otherwise, it must be a numeric literal
        token.Type = TokenType.Literal;

        bool isInteger; // whether the numeric is an integer
        ulong integerValue = 0;
        
        // if it started with zero, it might be a hex number 0x...
        bool isHex = c == '0' && char.ToLowerInvariant(Char) == 'x';
        if(isHex)
        {
          uint digitValue;

          isInteger = true;
          if(!IsHexDigit(NextChar(), out digitValue)) // skip the 0x and make sure a hex digit follows
          {
            AddMessage(Diagnostic.InvalidNumber, token.Start);
          }
          else
          {
            integerValue   = digitValue;
            bool overflow = false;
            while(IsHexDigit(NextChar(), out digitValue))
            {
              // if the value contains anything in the top four bits that are about to be shifted out, it's an overflow
              if((integerValue & 0xF000000000000000) != 0) overflow = true;
              integerValue = (integerValue << 4) + digitValue;
            }
            if(overflow) AddMessage(Diagnostic.IntegralConstantTooLarge, token.Start);
          }
        }
        else // if it wasn't a hex digit, it's either an integer or a real
        {
          StringBuilder sb = new StringBuilder(16);
          sb.Append(c); // we skipped past the first character (c) above, so add it
          isInteger = c != '.'; // if the first character was a period, this isn't an integer

          int sawExponent = -1; // the location where the exponent character ('e') was seen (or -1 if it wasn't seen)
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
              sawExponent = sb.Length+1; // +1 because it hasn't been added yet
              isInteger   = false; // if we're doing exponentation, we won't consider this an integer
            }
            else if(c == '.') // if there's a decimal point, it's not an integer
            {
              isInteger = false;
            }

            sb.Append(c);
            NextChar();
          }

          Type realType = null; // the type of value that we are about to parse (used for the error message)
          try
          {
            string numberString = sb.ToString();
            char suffix = char.ToLowerInvariant(Char); // get the character immediately after the number

            // if the suffix indicates a non-integer, set isInteger to false (eg, in the case of "5m")
            if(suffix == 'f' || suffix == 'm' || suffix == 'd') isInteger = false;

            if(isInteger) // if it's an integer, parse it into a ulong
            {
              integerValue = ulong.Parse(numberString, CultureInfo.InvariantCulture);
            }
            else if(suffix == 'm') // a decimal value
            {
              realType = typeof(decimal);
              NextChar(); // skip past the suffix
              // decimal.Parse() won't handle exponents, so if we've got one, send it through double.Parse()
              token.Data.Value = sawExponent == -1 ? decimal.Parse(numberString, CultureInfo.InvariantCulture)
                                   : new decimal(double.Parse(numberString, CultureInfo.InvariantCulture));
              break; // return the token
            }
            else // either a float or double
            {
              realType = typeof(double); // parse it into a double first
              double realValue = double.Parse(numberString, CultureInfo.InvariantCulture);

              if(suffix == 'f') // if there was a 'float' suffix, convert it to float
              {
                NextChar(); // skip the suffix

                float floatValue = (float)realValue;
                if(float.IsInfinity(floatValue)) // if it didn't fit in the float, give an error
                {
                  AddMessage(Diagnostic.RealConstantTooLarge, token.Start, Diagnostic.TypeName(typeof(float)));
                }
                token.Data.Value = floatValue;
              }
              else
              {
                if(suffix == 'd') NextChar(); // if there's a 'double' suffix, skip it.
                token.Data.Value = realValue;
              }
              break; // return the token
            }
          }
          catch(FormatException)
          {
            AddMessage(Diagnostic.InvalidNumber, token.Start);
          }
          catch(OverflowException) // if an overflow occurred, give an error and assign a value of the appropriate type
          {
            if(isInteger)
            {
              AddMessage(Diagnostic.IntegralConstantTooLarge, token.Start);
              token.Data.Value = 0;
            }
            else
            {
              AddMessage(Diagnostic.RealConstantTooLarge, token.Start, Diagnostic.TypeName(realType));
              token.Data.Value = realType == typeof(double) ? 0.0 : (object)0m;
            }
            break; // return the token
          }
        }

        Debug.Assert(isInteger); // if we got here, it should be an integer
        
        bool unsigned = false, longFlag = false; // the 'u' and 'L' flags

        c = char.ToLowerInvariant(Char); // get the suffix, if any
        if(c == 'u') unsigned = true;
        else if(c == 'l')
        {
          if(Char == 'l') AddMessage(Diagnostic.UseUppercaseL); // if it's lowercase, warn about how 'l' looks like '1'
          longFlag = true;
        }

        if(unsigned || longFlag) // if we got one suffix, skip past it and check for another one
        {
          NextChar();
          c = char.ToLowerInvariant(Char);
          if(unsigned && c == 'l') longFlag = true;
          else if(longFlag && c == 'u') unsigned = true;
        }

        if(unsigned && longFlag) NextChar(); // if we got a second one, skip it as well

        if(integerValue > (ulong)long.MaxValue) // now convert the number to the smallest type able to hold it,
        {                                       // respecting the user's suffix flags
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

      #region String literal
      else if(c == '"')
      {
        StringBuilder sb = new StringBuilder();
        c = NextChar(); // skip the opening quote
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

      #region Character literal
      else if(c == '\'')
      {
        c = NextChar(); // skip the opening quote
        if(c == '\'') AddMessage(Diagnostic.EmptyCharacterLiteral); // complain if it's empty
        else if(c == '\n') AddMessage(Diagnostic.NewlineInConstant); // complain if there's a newline
        else
        {
          NextChar(); // skip over the character value
          if(c == '\\') c = ProcessEscape(false); // if it was a backslash, though, read in the escape code

          if(Char != '\'') // we should now be at the closing quote. if not, complain
          {
            FilePosition expectedAt = Position; // save the position for the error message
            while(Char != '\'' && Char != '\n' && Char != 0) NextChar();
            // if we found the end, complain that it's too long. otherwise, complain that we couldn't find it
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

      #region Preprocessor directives
      else if(c == '#')
      {
        if(!firstOnLine) // if this isn't the first token on the line, complain and skip it
        {
          AddMessage(Diagnostic.PPNotFirstToken);
          SkipToEOL();
          goto continueScanning;
        }

        string directive = ReadPPDirective(); // try to read the directive
        if(directive == null) goto continueScanning; // if we can't, continue scanning (an error was already reported)

        SkipWhitespace(false); // skip whitespace after the directive, so we should be at \n or the first parameter
        switch(directive)
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
              lineOverride   = TokenData.DefaultLine; // reset to default
              sourceOverride = null;

              if(m.Groups[1].Value == "hidden") // '#line hidden' means to hide lines from the debugger
              {
                lineOverride = TokenData.HiddenLine;
              }
              else if(m.Groups[1].Value != "default") // if it's not '#line default'...
              {
                try
                {
                  lineOverride   = int.Parse(m.Groups[1].Value); // read the line number
                  sourceOverride = m.Groups[2].Value.Trim(); // and read the file name
                  if(sourceOverride.Length == 0) sourceOverride = null; // if the file name was empty, set it to null
                }
                catch(OverflowException) // if the line number was too big, complain about it
                {
                  AddMessage(Diagnostic.IntegralConstantTooLarge, token.Start);
                }
              }
            }

            goto continueScanning; // in any case, we've consumed the rest of the line, so continue scanning
          }

          case "if": case "elif":
          {
            if(directive[0] == 'e') // if it's #elif
            {
              // complain if there was no #if preceding it, or we've already seen the #else
              if(ppNesting.Count == 0 || ppNesting.Peek() == PPResult.Else)
              {
                AddMessage(Diagnostic.UnexpectedPPDirective, token.Start);
              }
              else if(ppNesting.Peek() == PPResult.True) // if a previous #if/#elif was true, skip this block
              {
                PPSkip(false);
                goto continueScanning;
              }
            }

            bool conditional;
            if(!PPEvaluate(ReadRestOfLine(true), out conditional))
            {
              // if the expression was invalid, given an error and treat it as false
              AddMessage(Diagnostic.InvalidPPExpression, token.Start);
            }
            
            if(directive[0] == 'e') ppNesting.Pop(); // if it's #elif, pop value of the previous #if/#elif first, so
            ppNesting.Push(conditional ? PPResult.True : PPResult.False); // we end up replacing the previous value

            if(!conditional) PPSkip(false); // if the expression was false, skip this block
            goto continueScanning;
          }

          case "else":
            // complain if there was no #if preceding this #else, or we've already seen an #else
            if(ppNesting.Count == 0 || ppNesting.Peek() == PPResult.Else)
            {
              AddMessage(Diagnostic.UnexpectedPPDirective, token.Start);
            }
            else if(ppNesting.Peek() == PPResult.True) PPSkip(true); // skip this block if a preceding #if was true
            else FinishPPLine(); // otherwise, skip to the end of the the #else line and look for tokens
            goto continueScanning;

          case "endif":
            // complain if there was no #if preceding this #endif
            if(ppNesting.Count == 0) AddMessage(Diagnostic.UnexpectedPPDirective, token.Start);
            else ppNesting.Pop(); // otherwise, pop off the value
            FinishPPLine(); // finish the #endif line and continue scanning
            goto continueScanning;

          case "region":
            regionDepth++; // we don't process regions except to ensure that all opened regions are closed
            SkipToEOL();
            goto continueScanning;

          case "endregion":
            if(regionDepth == 0) AddMessage(Diagnostic.UnexpectedPPDirective, token.Start);
            else regionDepth--;
            SkipToEOL();
            goto continueScanning;

          case "pragma":
          {
            string line = ReadRestOfLine(false); // read the pragma line

            Match m = warningRe.Match(line);
            if(m.Success) // if it's a valid #pragma warning line
            {
              bool disable = m.Groups[1].Value == "disable"; // see if we're disabling or restoring

              if(m.Groups[2].Success) // if there was a list of warning numbers, disable/restore that set
              {
                // parse the list of warning numbers and disable/restore each one
                string[] warningStrings = m.Groups[2].Value.Split(',');
                for(int i=0; i<warningStrings.Length; i++)
                {
                  int warningCode;
                  // issue a diagnostic if any of the warning codes do not reference valid warnings
                  if(!int.TryParse(warningStrings[i], out warningCode) || !Diagnostic.IsValidWarning(warningCode))
                  {
                    AddMessage(Diagnostic.InvalidWarningCode, token.Start, warningStrings[i].Trim());
                  }
                  else if(disable) Compiler.Options.DisableWarning(warningCode);
                  else Compiler.Options.RestoreWarning(warningCode);
                }
              }
              // otherwise, there was no list of numbers, so disable or restore all warnings
              else if(disable) Compiler.Options.DisableWarnings();
              else Compiler.Options.RestoreWarnings();
            }
            // it wasn't a valid #pragma warning line, but see if it might have been a mistake
            else if(line.StartsWith("warning", StringComparison.InvariantCultureIgnoreCase))
            {
              AddMessage(Diagnostic.InvalidWarningPragma, token.Start); // if so, issue a diagnostic about it
            }
            else // otherwise, assume it's just an unrecognized pragma
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

            string name = ReadIdentifier(); // read the identifier that comes after #define/#undef
            if(name == null) AddMessage(Diagnostic.ExpectedIdentifier); // complain if it's not there
            else
            {
              if(directive[0] == 'd') Compiler.Options.Define(name);
              else Compiler.Options.Undefine(name);
            }
            FinishPPLine();
            goto continueScanning;
          }

          case "warning": case "error":
            AddMessage(directive[0] == 'w' ? Diagnostic.UserWarning : Diagnostic.UserError, token.Start,
                       ReadRestOfLine(false));
            goto continueScanning;

          default:
            AddMessage(Diagnostic.PPDirectiveExpected, token.Start); // unknown PP directive
            SkipToEOL(); // skip the line and continue
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
          case '{': token.Type = TokenType.LCurly; break;
          case '}': token.Type = TokenType.RCurly; break;
          case '[': token.Type = TokenType.LSquare; break;
          case ']': token.Type = TokenType.RSquare; break;
          case ';': token.Type = TokenType.Semicolon; break;
          case ',': token.Type = TokenType.Comma; break;
          case '.': token.Type = TokenType.Period; break; // this should be handled by the numeric code

          case '-':
            if(NextChar() == '-') token.Type = TokenType.Decrement;
            else { token.Type = TokenType.Minus; checkAssign=true; skipChar=false; }
            break;
          case '+':
            if(NextChar() == '+') token.Type = TokenType.Increment;
            else { token.Type = TokenType.Plus; checkAssign=true; skipChar=false; }
            break;
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
            else if(Char == '<') { token.Type = TokenType.LShift; checkAssign=true; }
            else { token.Type = TokenType.LessThan; skipChar=false; }
            break;
          case '>':
            if(NextChar() == '=') token.Type = TokenType.GreaterOrEq;
            else if(Char == '>') { token.Type = TokenType.RShift; checkAssign=true; }
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
    if(Compiler.Options.ShouldShow(diagnostic))
    {
      AddMessage(diagnostic.ToMessage(Compiler.Options.TreatWarningsAsErrors, SourceName, position, args));
    }
  }

  /// <summary>Ensures that the rest of the line is a valid finish for a preprocessor line.</summary>
  /// <remarks>A preprocessor line can end with a single-line comment or a newline.</remarks>
  void FinishPPLine()
  {
    if(SkipWhitespace(false) != '\n' && Char != 0)
    {
      if(Char == '/' && NextChar() == '/') SkipToEOL();
      else AddMessage(Diagnostic.PPEndExpected);
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

      string token = m.Groups[1].Value;
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
      return index == tokens.Count; // if not all tokens were consumed, it must be an invalid expression
    }
    catch(FormatException)
    {
      return false;
    }
  }

  bool PPEvaluateOr(List<string> tokens, ref int index)
  {
    bool value = PPEvaluateAnd(tokens, ref index);
    while(index < tokens.Count && tokens[index] == "||")
    {
      index++;
      value = PPEvaluateAnd(tokens, ref index) || value;
    }
    return value;
  }

  bool PPEvaluateAnd(List<string> tokens, ref int index)
  {
    bool value = PPEvaluateEquality(tokens, ref index);
    while(index < tokens.Count && tokens[index] == "&&")
    {
      index++;
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
      else break;

      index++;
      bool rhs = PPEvaluateUnary(tokens, ref index);
      value = not ? value != rhs : value == rhs;
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
      if(depth == 0) SaveState(); // if this might be where we stop, save the state. we do it before advancing past \n
      NextChar();                 // so that firstOnLine will be set to true after returning
      if(SkipWhitespace(false) == '#') // if this line starts with a PP directive, process it
      {
        string ident = ReadPPDirective();

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
          if(skippingElse)
          {
            NextChar(); // skip the newline so the error shows up on the right line
            AddMessage(Diagnostic.UnexpectedPPDirective); // we shouldn't find #elif/#else after #else
          }
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
    bool hadEscape;
    return ReadIdentifier(out hadEscape);
  }

  /// <summary>Reads an identifier, assusing the scanner is positioned at one.</summary>
  /// <param name="hadEscape">A variable that receives whether the identifier had a \uXXXX escape code.</param>
  string ReadIdentifier(out bool hadEscape)
  {
    hadEscape = false;

    char c = Char;
    if(!IsIdentifierStart(c) && c != '\\') return null;

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
            AddMessage(Diagnostic.UnexpectedCharacter, start, '\\');
            return null;
          }
          break; // but if we have part of an identifier in 'sb', we'll return it first
        }
        sb.Append(ProcessEscape(false));
        c = Char;
        hadEscape = true;
      }
      else
      {
        sb.Append(c);
        c = NextChar();
      }
    } while(IsIdentifierRest(c) || c == '\\');

    return sb.ToString();
  }

  /// <summary>If the scanner is positioned at the '#' of a preprocessor directive, reads the directive name.</summary>
  string ReadPPDirective()
  {
    NextChar(); // skip over the '#'
    if(SkipWhitespace(false) == '\n' || Char == 0) // skip whitespace after the '#'
    {
      AddMessage(Diagnostic.PPDirectiveExpected);
      return null;
    }

    StringBuilder sb = new StringBuilder(9);
    do
    {
      sb.Append(Char);
    } while(!char.IsWhiteSpace(NextChar()) && Char != 0);

    return sb.ToString();
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

  enum PPResult : byte { True, False, Else }

  /// <summary>Overrides the current source name with this, if it's not equal to null.</summary>
  string sourceOverride;
  /// <summary>The values of evaluating preprocessor #if/#elif tokens.</summary>
  Stack<PPResult> ppNesting = new Stack<PPResult>();
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

  /// <summary>Determines whether the given character is a legal start character for an identifier.</summary>
  static bool IsIdentifierStart(char c)
  {
    return char.IsLetter(c) || c == '_' || char.GetUnicodeCategory(c) == UnicodeCategory.LetterNumber;
  }

  /// <summary>Determines whether the given character is a legal character for the part of an identifier after the
  /// first character.
  /// </summary>
  static bool IsIdentifierRest(char c)
  {
    if(char.IsLetterOrDigit(c)) return true;

    switch(char.GetUnicodeCategory(c))
    {
      case UnicodeCategory.LetterNumber:
      case UnicodeCategory.NonSpacingMark:
      case UnicodeCategory.SpacingCombiningMark:
      case UnicodeCategory.ConnectorPunctuation:
      case UnicodeCategory.Format:
        return true;
      default: return false;
    }
  }

  /// <summary>A map of strings to keyword tokens.</summary>
  static readonly Dictionary<string, TokenType> keywords =
    new Dictionary<string, TokenType>(TokenType.KeywordEnd-TokenType.KeywordStart);

  static readonly Regex lineRe = new Regex(@"^(hidden|default|\d+(?:\s*""([^""]+)"")?)\s*$",
                                           RegexOptions.Compiled | RegexOptions.Singleline);
  static readonly Regex ppExprRe =
    new Regex(@"\s*(\|\||&&|==|!=|!|\(|\)|true|false|[_\p{Ll}\p{Lu}\p{Lt}\p{Lo}\p{Nl}\p{Lm}](?:[\w\p{Nl}\p{Cf}\p{Mn}\p{Mc}]|\\[uU][0-9a-fA-F]{1,4})+)\s*",
              RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

  static readonly Regex ppIdentEscapeRe = new Regex(@"\\[uU][0-9a-fA-F]{1,4}", RegexOptions.Singleline);

  static readonly Regex warningRe = new Regex(@"^warning\s+(disable|restore)(?:\s+(\d+(?:\s*,\s*\d+)*))?\s*$",
                                              RegexOptions.CultureInvariant | RegexOptions.Singleline);
}
#endregion

} // namespace Scripting.CSharper