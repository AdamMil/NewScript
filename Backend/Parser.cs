using System;
using System.Collections.Generic;
using System.Text;

namespace Scripting.AST
{

#region IParser
/// <summary>An interface that represents a parser.</summary>
public interface IParser
{
  /// <summary>Parses the entire input stream into a syntax tree.</summary>
  /// <returns>A syntax tree if there is any input, or null if there is no input.</returns>
  /// <remarks>This is called to parse the entire input at once.</remarks>
  ASTNode ParseProgram();
  /// <summary>Parses a single top-level sentence from the input into a syntax tree.</summary>
  /// <returns>A syntax tree if there is any input, or null if there is no input.</returns>
  ASTNode ParseOne();
  /// <summary>Parses a single expression into a syntax tree.</summary>
  /// <returns>A syntax tree if there is any input, or null if there is no input.</returns>
  /// <remarks>An expression does not typically allow statements such as variable assignment or
  /// declarations/definitions of functions, classes, etc, and as such, this method would refuse to parse them.
  /// However in some languages, everything is an expression, which makes this method equivalent to
  /// <see cref="ParseOne"/>.
  /// </remarks>
  ASTNode ParseExpression();
}
#endregion

#region ParserBase
/// <summary>A simple base class for parsers.</summary>
public abstract class ParserBase<CompilerType,TokenType>
  : CompilerUserBase<CompilerType>, IParser where CompilerType : CompilerBase
{
  protected ParserBase(CompilerType compiler, IScanner<TokenType> scanner) : base(compiler)
  {
    if(scanner == null) throw new ArgumentNullException();
    this.scanner = scanner;
  }

  public abstract ASTNode ParseProgram();
  public abstract ASTNode ParseOne();
  public abstract ASTNode ParseExpression();

  protected IScanner<TokenType> Scanner
  {
    get { return scanner; }
  }

  readonly IScanner<TokenType> scanner;
}
#endregion

#region BufferedParserBase
/// <summary>A parser base class that provides a configurable lookahead buffer.</summary>
public abstract class BufferedParserBase<CompilerType,TokenType>
  : ParserBase<CompilerType,TokenType> where CompilerType : CompilerBase
{
  /// <summary>A value used to specify an infinitely large lookahead buffer.</summary>
  protected const int Infinite = -1;

  /// <summary>Initializes this parser and advances it to the first token.</summary>
  /// <param name="lookahead">The number of tokens to keep in a lookahead buffer. Pass <see cref="Infinite"/> if you
  /// need the entire token stream to be available at once.
  /// </param>
  protected BufferedParserBase(CompilerType compiler, IScanner<TokenType> scanner, int lookahead)
    : base(compiler, scanner)
  {
    if(lookahead == Infinite)
    {
      List<TokenType> tokens = new List<TokenType>();
      TokenType token;
      while(scanner.NextToken(out token)) tokens.Add(token);
      this.tokenBuffer = tokens.ToArray();
      this.lookahead   = tokenBuffer.Length;
    }
    else if(lookahead < 0) throw new ArgumentOutOfRangeException();
    else tokenBuffer = new TokenType[lookahead];

    NextToken();
  }

  /// <summary>The current token.</summary>
  protected TokenType Token;

  /// <summary>Gets whether all tokens have been exhausted.</summary>
  protected bool EOF
  {
    get { return reachedEOF && lookahead == 0; }
  }

  /// <summary>Gets a token from the lookahead buffer.</summary>
  /// <param name="lookahead">The distance to look ahead. If equal to zero, this method simply returns
  /// <see cref="Token"/>.
  /// </param>
  protected TokenType GetToken(int lookahead)
  {
    if(lookahead == 0)
    {
      return Token;
    }
    else
    {
      EnsureLookahead(lookahead);
      return tokenBuffer[GetIndex(lookahead)];
    }
  }

  /// <summary>Advances to the next token.</summary>
  protected void NextToken()
  {
    if(lookahead == 0) Scanner.NextToken(out Token);
    else
    {
      Token = tokenBuffer[tail];
      if(++tail == tokenBuffer.Length) tail = 0;
      lookahead--;
    }
  }

  /// <summary>Ensures that there are at least <paramref name="count"/> tokens in the lookahead buffer.</summary>
  /// <param name="count">The number of tokens in addition to the current token to ensure exist in the lookahead buffer.</param>
  void EnsureLookahead(int count)
  {
    if(count < 0 || count > tokenBuffer.Length) throw new ArgumentOutOfRangeException();
    while(lookahead < count)
    {
      if(!Scanner.NextToken(out tokenBuffer[head])) reachedEOF = true;
      if(++head == tokenBuffer.Length) head = 0;
      lookahead++;
    }
  }

  /// <summary>Converts a valid lookahead index to the physical index within the buffer.</summary>
  int GetIndex(int lookahead)
  {
    lookahead += tail;
    if(lookahead >= tokenBuffer.Length) lookahead -= tokenBuffer.Length;
    return lookahead;
  }

  readonly TokenType[] tokenBuffer;
  int head, tail, lookahead;
  bool reachedEOF;
}
#endregion

} // namespace Scripting.AST