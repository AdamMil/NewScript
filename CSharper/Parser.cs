using System;
using System.Collections.Generic;
using Scripting.AST;

namespace Scripting.CSharper
{

using Token = AST.Token<TokenType,TokenData>;

public class Parser : ParserBase<Compiler,Scanner,Token>, IParser
{
  /// <summary>Initializes the parser and advances to the first token.</summary>
  public Parser(Compiler compiler, Scanner scanner) : base(compiler, scanner)
  {
    NextToken();
  }
  
  /// <summary>Parses a set of source files into a list of <see cref="SourceFileNode"/>.</summary>
  public override ASTNode ParseProgram()
  {
    ASTNode sourceFiles = null;
    while(!Is(TokenType.EOD)) ASTNode.AddSibling(ref sourceFiles, ParseOne());
    return sourceFiles;
  }

  /// <summary>Parses an entire source file and returns it in a <see cref="SourceFileNode"/>.</summary>
  /// <returns></returns>
  public override ASTNode ParseOne()
  {
    string sourceName = Scanner.SourceName;
    NamespaceNode topNs = new NamespaceNode(null); // an null-named namespace is at the root of each file
    ParseNamespaceInterior(topNs);
    if(!TryEat(TokenType.EOF)) AddMessage(Diagnostic.ExpectedSyntax, "end of file");
    return SetSpan(sourceName, new SourceFileNode(topNs));
  }

  public override ASTNode ParseExpression()
  {
    throw new NotImplementedException();
  }

  struct Span
  {
    public Span(string sourceName, FilePosition start, FilePosition end)
    {
      SourceName = sourceName;
      Start      = start;
      End        = end;
    }

    public string SourceName;
    public FilePosition Start, End;
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic)
  {
    AddMessage(diagnostic, token.Start, new object[0]);
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic, params object[] args)
  {
    AddMessage(diagnostic, token.Start, args);
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic, FilePosition position, params object[] args)
  {
    if(Compiler.Options.ShouldShow(diagnostic))
    {
      AddMessage(diagnostic.ToMessage(Compiler.Options.TreatWarningsAsErrors, token.SourceName, position, args));
    }
  }

  // a namespace contains extern aliases, followed by using statements, followed by type declarations
  void ParseNamespaceInterior(NamespaceNode nsNode)
  {
    // first parse all the extern aliases
    List<string> list = null;
    while(TryEat(TokenType.Extern))
    {
      string name;
      if(!EatPseudoKeyword("alias") || !EatIdentifier(out name))
      {
        RecoverTo(TokenType.Semicolon);
      }
      else
      {
        AddItem(ref list, name);
      }
      EatSemicolon();
    }
    nsNode.ExternAliases = ToArray(list);

    // then parse all the using statements. a using statement is 'using IDENT = DOTTED_IDENT;' or 'using DOTTED_IDENT;'
    while(TryEat(TokenType.Using))
    {
      Span span = PreviousSpan;

      string alias, typeOrNs = null;
      bool wasDotted;

      if(!ParseIdentifierOrDotted(out alias, out wasDotted)) RecoverTo(TokenType.Semicolon);
      else if(!wasDotted && TryEat(TokenType.Equals)) // if the next token is '=', it looks like a using alias
      {
        if(!ParseSimpleName(out typeOrNs)) RecoverTo(TokenType.Semicolon);
      }
      else // it wasn't an alias
      {
        typeOrNs = alias;
        alias    = null;
      }

      EatSemicolon();
      if(typeOrNs == null) continue; // if we failed to read at least the type/namespace name, skip this one

      nsNode.AddUsingNode(SetSpan(span, previousToken.End, new UsingNode(typeOrNs, alias)));
    }
  }

  /// <summary>Adds an item to a list, instantiating the list if it's null.</summary>
  void AddItem<T>(ref List<T> list, T item)
  {
    if(list == null) list = new List<T>();
    list.Add(item);
  }

  /// <summary>Clears the given list, if it's not null.</summary>
  void ClearList<T>(List<T> list)
  {
    if(list != null) list.Clear();
  }

  /// <summary>Converts the given list to an array, or returns null if the list is null or empty.</summary>
  T[] ToArray<T>(List<T> list)
  {
    return list == null || list.Count == 0 ? null : list.ToArray();
  }

  /// <summary>Returns the span of the previous token.</summary>
  Span PreviousSpan
  {
    get { return new Span(previousToken.SourceName, previousToken.Start, previousToken.End); }
  }

  /// <summary>Gets whether the current token is a keyword.</summary>
  bool IsKeyword
  {
    get { return type >= TokenType.KeywordStart && type < TokenType.KeywordEnd; }
  }

  /// <summary>Attempts to consume an identifier.</summary>
  /// <returns>True if an identifier was consumed and false if not.</returns>
  bool EatIdentifier(out string identifier)
  {
    if(Is(TokenType.Identifier))
    {
      identifier = (string)token.Data.Value;
      NextToken();
      return true;
    }
    else
    {
      identifier = null;
      if(IsKeyword) AddMessage(Diagnostic.ExpectedIdentGotKeyword, token.Data.Value);
      else AddMessage(Diagnostic.ExpectedIdentifier);
      return false;
    }
  }

  /// <summary>Attempts to consume an identifier with the given value.</summary>
  /// <returns>Returns true if the identifier was consumed, and false if not.</returns>
  bool EatPseudoKeyword(string value)
  {
    if(IsIdentifier(value))
    {
      NextToken();
      return true;
    }
    else
    {
      AddMessage(Diagnostic.ExpectedSyntax, value);
      return false;
    }
  }

  /// <summary>Attempts to consume a semicolon.</summary>
  /// <returns>True if a semicolon was consumed and false otherwise.</returns>
  bool EatSemicolon()
  {
    if(TryEat(TokenType.Semicolon)) return true;
    AddMessage(Diagnostic.ExpectedSemicolon);
    return false;
  }

  /// <summary>Returns whether the current token is of the given type.</summary>
  bool Is(TokenType type)
  {
    return this.type == type;
  }

  /// <summary>Determines whether the current token is an identifier with the given value.</summary>
  bool IsIdentifier(string value)
  {
    return Is(TokenType.Identifier) && string.Equals((string)token.Data.Value, value, System.StringComparison.Ordinal);
  }

  /// <summary>Parses an identifier or dotted identifier.</summary>
  bool ParseIdentifierOrDotted(out string value, out bool wasDotted)
  {
    wasDotted = false;
    if(!EatIdentifier(out value)) return false;
    while(TryEat(TokenType.Period))
    {
      string nextPart;
      if(!EatIdentifier(out nextPart)) return false;
      value += "."+nextPart;
      wasDotted = true;
    }
    return true;
  }

  /// <summary>Advances to the next token and returns its type.</summary>
  TokenType NextToken()
  {
    previousToken = token;
    if(!Scanner.NextToken(out token))
    {
      token.Type = TokenType.EOD;
    }
    return type = token.Type;
  }

  /// <summary>Attempts to skip tokens until it finds a token of the given type. This method will not skip past
  /// <see cref="TokenType.EOF"/> or <see cref="TokenType.EOD"/>.
  /// </summary>
  bool RecoverTo(TokenType type)
  {
    while(type != TokenType.EOF && type != TokenType.EOD && !Is(type)) NextToken();
    return Is(type);
  }

  /// <summary>Attempts to consume a token of the given type. No diagnostics are reported if the current token is not
  /// of the given type.
  /// </summary>
  /// <returns>True if the token was consumed, and false if not.</returns>
  bool TryEat(TokenType type)
  {
    if(this.type == type)
    {
      NextToken();
      return true;
    }
    else return false;
  }

  Token token, previousToken;
  TokenType type;

  static T SetSpan<T>(string sourceName, T node) where T : ASTNode
  {
    node.SourceName = sourceName;
    return node;
  }

  static T SetSpan<T>(Span span, FilePosition end, T node) where T : ASTNode
  {
    span.End = end;
    node.SetSpan(span.SourceName, span.Start, span.End);
    return node;
  }
}

} // namespace Scripting.CSharper