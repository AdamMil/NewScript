using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Scripting.AST;

namespace Scripting.CSharper
{

using Token = AST.Token<TokenType,TokenData>;

#region Identifier
public sealed class Identifier
{
  public Identifier(string name, string sourceName, Position start, Position end)
  {
    Name  = name;
    Scope = null;
    Span  = new FileSpan(sourceName, start, end);
  }

  public string Name, Scope;
  public FileSpan Span;
}
#endregion

#region Parser
public class Parser : BufferedParserBase<Compiler,Scanner,Token>, IParser
{
  /// <summary>The amount of lookahead we need to parse the language.</summary>
  const int LookaheadNeeded = 2;

  /// <summary>Initializes the parser and advances to the first token.</summary>
  public Parser(Compiler compiler, Scanner scanner) : base(compiler, scanner, LookaheadNeeded) { }

  #if DEBUG
  static Parser()
  {
    // verify some assumptions we make. first, ensure that modifier keywords are in the same order as the Modifier enum
    for(TokenType tt=TokenType.ModifierStart; tt<TokenType.ModifierEnd; tt++)
    {
      Debug.Assert(Enum.GetName(typeof(TokenType), tt) == Enum.GetName(typeof(Modifier), TokenToModifier(tt)));
    }
  }
  #endif

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
    NamespaceNode topNs = ParseNamespaceInterior(null); // an null-named namespace is at the root of each file
    if(!TryEat(TokenType.EOF)) AddMessage(Diagnostic.ExpectedSyntax, "end of file");
    EnsureNoXmlComment();
    return SetSpan(sourceName, new SourceFileNode(topNs));
  }

  public override ASTNode ParseExpression()
  {
    throw new NotImplementedException();
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic)
  {
    AddMessage(diagnostic, Token.Start, new object[0]);
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic, params object[] args)
  {
    AddMessage(diagnostic, Token.Start, args);
  }

  /// <summary>Adds a compiler message using the given diagnostic.</summary>
  void AddMessage(Diagnostic diagnostic, Position position, params object[] args)
  {
    if(Compiler.Options.ShouldShow(diagnostic))
    {
      AddMessage(diagnostic.ToMessage(Compiler.Options.TreatWarningsAsErrors, Token.SourceName, position, args));
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

  /// <summary>Returns the <see cref="FilePosition"/> of the current token.</summary>
  FilePosition Position
  {
    get { return new FilePosition(Token.SourceName, Token.Start); }
  }

  /// <summary>Returns the <see cref="FilePosition"/> of the previous token.</summary>
  FilePosition PreviousPosition
  {
    get { return new FilePosition(previousToken.SourceName, previousToken.Start); }
  }

  /// <summary>Gets whether the current token is a keyword.</summary>
  bool IsKeyword
  {
    get { return Token.Type >= TokenType.KeywordStart && Token.Type < TokenType.KeywordEnd; }
  }

  /// <summary>Gets whether the current token is a keyword.</summary>
  bool IsDeclKeyword
  {
    get { return Token.Type >= TokenType.DeclStart && Token.Type < TokenType.DeclEnd; }
  }

  /// <summary>Gets whether the current token is a keyword.</summary>
  bool IsModifierKeyword
  {
    get { return Token.Type >= TokenType.ModifierStart && Token.Type < TokenType.ModifierEnd; }
  }

  /// <summary>Gets whether the current token is a keyword.</summary>
  bool IsTypeKeyword
  {
    get { return Token.Type >= TokenType.TypeStart && Token.Type < TokenType.TypeEnd; }
  }

  /// <summary>Determines whether the current token could start a type declaration.</summary>
  bool CouldBeStartOfDeclaration()
  {
    // identifier for type names and '[' for attributes
    return IsModifierKeyword || IsTypeKeyword || IsDeclKeyword ||
           Is(TokenType.Identifier) || Is(TokenType.LSquare) || Is(TokenType.Tilde);
  }

  /// <summary>Consumes a token of the given type.</summary>
  void Eat(TokenType type)
  {
    if(!Is(type)) throw new InvalidOperationException(); // the token MUST be of the given type
    NextToken();
  }

  /// <summary>Attempts to consume an identifier.</summary>
  /// <returns>True if an identifier was consumed and false if not.</returns>
  bool EatIdentifier(out string identifier)
  {
    if(Is(TokenType.Identifier))
    {
      identifier = (string)Token.Data.Value;
      return true;
    }
    else
    {
      identifier = null;
      if(IsKeyword) AddMessage(Diagnostic.ExpectedIdentGotKeyword, Token.Data.Value);
      else AddMessage(Diagnostic.ExpectedIdentifier);
      return false;
    }
  }

  /// <summary>Attempts to consume an open curly brace.</summary>
  /// <returns>True if an open curly brace was consumed and false otherwise.</returns>
  bool EatOpenCurly()
  {
    if(TryEat(TokenType.LCurly)) return true;
    AddMessage(Diagnostic.ExpectedOpenCurly);
    return false;
  }

  /// <summary>Attempts to consume a closing curly brace.</summary>
  /// <returns>True if a closing curly brace was consumed and false otherwise.</returns>
  bool EatClosingCurly()
  {
    if(TryEat(TokenType.RCurly)) return true;
    AddMessage(Diagnostic.ExpectedClosingCurly);
    return false;
  }

  /// <summary>Attempts to consume a closing parenthesis.</summary>
  /// <returns>True if a closing parenthesis was consumed and false otherwise.</returns>
  bool EatClosingParenthesis()
  {
    if(TryEat(TokenType.RParen)) return true;
    AddMessage(Diagnostic.ExpectedClosingParen);
    return false;
  }

  /// <summary>Attempts to consume a closing square bracket.</summary>
  /// <returns>True if a closing square bracket was consumed and false otherwise.</returns>
  bool EatClosingSquare()
  {
    if(TryEat(TokenType.RSquare)) return true;
    AddMessage(Diagnostic.ExpectedCharacter, ']');
    return false;
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

  /// <summary>If any XML comment is defined, remove it and warn that it wasn't placed on a valid element.</summary>
  void EnsureNoXmlComment()
  {
    if(Scanner.XmlComment != null)
    {
      AddMessage(Diagnostic.MisplacedXmlComment);
      Scanner.ClearXmlComment();
    }
  }

  /// <summary>Returns whether the current token is of the given type.</summary>
  bool Is(TokenType type)
  {
    return Token.Type == type;
  }

  /// <summary>Returns whether the given token is of the given type.</summary>
  bool Is(int lookahead, TokenType type)
  {
    return GetToken(lookahead).Type == type;
  }

  /// <summary>Returns whether the current token is of one of the given types.</summary>
  bool Is(params TokenType[] types)
  {
    return Array.IndexOf(types, Token.Type) != -1;
  }

  /// <summary>Determines whether the current token is an identifier with the given value.</summary>
  bool IsIdentifier(string value)
  {
    return Is(TokenType.Identifier) && string.Equals((string)Token.Data.Value, value, System.StringComparison.Ordinal);
  }

  /// <summary>Returns whether the next token is of the given type.</summary>
  bool NextIs(TokenType type)
  {
    return GetToken(1).Type == type;
  }

  /// <summary>Determines whether the next token is an identifier with the given value.</summary>
  bool NextIsIdentifier(string value)
  {
    Token token = GetToken(1);
    return token.Type == TokenType.Identifier && 
           string.Equals((string)token.Data.Value, value, System.StringComparison.Ordinal);
  }

  // AttributeSection = ((IDENT|KEYWORD) :)? Attribute (, Attribute)*
  // Attribute = TypeName ConstructorCall?
  ASTNode ParseAttributeSection()
  {
    AttributeTarget target;
    bool validTarget = ParseAttributeTarget(out target);

    ASTNode attributes = null;
    while(true) // for each attribute in the attribute block
    {
      FilePosition start = Position;

      TypeBase typeName;
      ParseTypeName(out typeName);

      if(typeName == null)
      {
        RecoverTo(TokenType.RSquare, TokenType.Comma);
      }
      else
      {
        ASTNode[] arguments = null, namedArguments = null;
        string[] names = null;
        if(Is(TokenType.LParen)) ParseConstructorCall(out arguments, out names, out namedArguments);
        ASTNode attribute = new AttributeNode(target, typeName, arguments, names, namedArguments);
        ASTNode.AddSibling(ref attributes, SetSpan(start, previousToken.End, attribute));
      }

      // if there are no more commas or there's a comma followed by a right bracket, we've got all the attributes
      // in this block
      if(!TryEat(TokenType.Comma) || Is(TokenType.RSquare)) break;
    }

    return validTarget ? attributes : null; // ignore attributes with an invalid target
  }
  
  // AttributeSections = ([AttributeSection])*
  ASTNode ParseAttributeSections()
  {
    ASTNode attributes = null;

    while(TryEat(TokenType.LSquare)) // for each attribute block
    {
      ASTNode attributeList = ParseAttributeSection();
      if(attributeList != null) ASTNode.AddSibling(ref attributes, attributeList);
      EatClosingSquare();
    }

    return attributes;
  }

  /// <summary>If the current token is a valid attribute target, it is returned in <paramref name="target"/> and
  /// consumed. Otherwise, <paramref name="target"/> is left unchanged.
  /// </summary>
  /// <returns>Returns true if no attribute target was specified, or a valid attribute target was specified. Returns
  /// false if an unknown target was specified.
  /// </returns>
  // AttributeTarget = (IDENT|KEYWORD) ':'
  bool ParseAttributeTarget(out AttributeTarget target)
  {
    target = AttributeTarget.Unknown;

    bool valid = true;
    if(NextIs(TokenType.Colon) && (IsKeyword || Is(TokenType.Identifier)))
    {
      switch((string)Token.Data.Value)
      {
        case "assembly": target = AttributeTarget.Assembly; break;
        case "event":    target = AttributeTarget.Event; break;
        case "field":    target = AttributeTarget.Field; break;
        case "method":   target = AttributeTarget.Method; break;
        case "param":    target = AttributeTarget.Param; break;
        case "property": target = AttributeTarget.Property; break;
        case "return":   target = AttributeTarget.Return; break;
        case "type":     target = AttributeTarget.Type; break;
        case "typevar":  target = AttributeTarget.TypeVar; break;
        default:
          AddMessage(Diagnostic.UnknownAttributeTarget, Token.Data.Value);
          valid = false;
          break;
      }
      Eat(TokenType.Identifier);
      Eat(TokenType.Colon);
    }
    return valid;
  }

  /// <summary>Parses a list of assembly attributes.</summary>
  ASTNode ParseGlobalAttributes()
  {
    ASTNode attributes = null;

    while(Is(TokenType.LSquare) && NextIsIdentifier("assembly") && Is(2, TokenType.Colon)) 
    {
      ASTNode attributeList = ParseAttributeSection();
      if(attributeList != null) ASTNode.AddSibling(ref attributes, attributeList);
      EatClosingSquare();
    }

    return attributes;
  }

  // ConstructorCall = '(' (PositionalArgs | NamedArgs | PositionalArgs, NamedArgs) ')'
  // PositionalArgs = Expression (, Expression)
  // NamedArgs = IDENT '=' Expression
  void ParseConstructorCall(out ASTNode[] arguments, out string[] names, out ASTNode[] namedArguments)
  {
    Eat(TokenType.LParen);

    List<ASTNode> argList = null, namedArgList = null;
    List<string> nameList = null;
    while(!Is(TokenType.RParen))
    {
      // it's an error if there's a comma before the first argument, or no comma before the others
      if(TryEat(TokenType.Comma))
      {
        if(argList == null && namedArgList == null)
        {
          AddMessage(Diagnostic.UnexpectedCharacter, previousToken.Start, ',');
        }
      }
      else if(argList != null || namedArgList != null) AddMessage(Diagnostic.ExpectedCharacter, ',');

      string keyword = null;
      if(Is(TokenType.Identifier) && NextIs(TokenType.Equals)) // if it's a keyword argument...
      {
        EatIdentifier(out keyword);
        TryEat(TokenType.Equals);
      }

      ASTNode expression = ParseExpression();
      if(expression == null)
      {
        RecoverTo(TokenType.RParen);
        break;
      }

      if(keyword != null) // if this is a named argument, add it to the named arguments list
      {
        AddItem(ref nameList, keyword);
        AddItem(ref namedArgList, expression);
      }
      else
      {
        if(namedArgList != null) AddMessage(Diagnostic.NamedArgumentExpected); // named arguments come after positional ones
        AddItem(ref argList, expression);
      }
    }

    EatClosingParenthesis();

    arguments     = ToArray(argList);
    names      = ToArray(nameList);
    namedArguments = ToArray(namedArgList);
  }

  /// <summary>Parses a possibly-dotted identifier.</summary>
  /// <param name="value">A variable that receives the identifier string. This may be set even in the case of an error.</param>
  /// <returns>Returns true if the name was successfully parsed, and false if there was some error.</returns>
  bool ParseDottedName(out Identifier value)
  {
    bool wasDotted;
    return ParseDottedName(out value, out wasDotted);
  }

  /// <summary>Parses a possibly-dotted identifier.</summary>
  /// <param name="value">A variable that receives the identifier string. This may be valid (non-null) even in the
  /// case of an error.
  /// </param>
  /// <param name="wasDotted">A variable that indicates that the name was dotted.</param>
  /// <returns>Returns true if the name was successfully parsed, and false if there was some error.</returns>
  // Dotted = IDENT (. IDENT)*
  bool ParseDottedName(out Identifier value, out bool wasDotted)
  {
    wasDotted = false;
    Position start = Token.Start;

    string name;
    bool success = EatIdentifier(out name);
    if(success)
    {
      while(TryEat(TokenType.Period))
      {
        string nextPart;
        if(!EatIdentifier(out nextPart))
        {
          success = false;
          break;
        }
        name += "."+nextPart;
        wasDotted = true;
      }
    }

    value = new Identifier(name, previousToken.SourceName, start, previousToken.End);
    return success;
  }

  /// <summary>Parses a class, struct, or interface declaration.</summary>
  // TypeDecl    = (class|struct|interface) IDENT (< TypeParam (, TypeParam)* >) (: Type (, Type)*)? (where Constraints)*
  // TypeParam   = AttributeSections? (+|-)? IDENT
  // Constraints = IDENT : Constraint (, Constraint)*
  // Constraint  = class|struct|new()|Type
  TypeDeclarationNode ParseClassDeclaration(ASTNode attributes, Modifier modifier, TokenType classType)
  {
    Eat(classType);

    SetAttributeTargets(ref attributes, AttributeTarget.Type);

    // parse the name
    Identifier name;
    if(!ParseIdentifier(out name) && name == null)
    {
      RecoverFromBadDeclaration();
      return null;
    }

    // generics and inheritance not yet implemented
    if(Is(TokenType.LessThan) || Is(TokenType.Colon)) throw new NotImplementedException();

    if(!EatOpenCurly())
    {
      RecoverFromBadDeclaration();
      return null;
    }

    TypeDeclarationNode typeNode = new TypeDeclarationNode(name,
      classType == TokenType.Class ? TypeType.Class
                                   : classType == TokenType.Struct ? TypeType.Struct : TypeType.Interface);
    while(CouldBeStartOfDeclaration())
    {
      ParseDeclaration(classType, ref typeNode.Events, ref typeNode.Fields, ref typeNode.Methods,
                       ref typeNode.Properties, ref typeNode.Types);
    }

    EatClosingCurly();
    return typeNode;
  }

  /// <summary>Parses a member or nested type declaration, adds it to the appropriate list, and returns it.</summary>
  // Declaration = EventDecl | FieldDecl | MethodDecl | PropertyDecl | TypeDecl
  // MethodDecl = (Type? | ~) Identifier '(' ParamList ')' (; | { Statement* })
  void ParseDeclaration(TokenType containerType, ref ASTNode events, ref ASTNode fields, ref ASTNode methods,
                        ref ASTNode properties, ref ASTNode types)
  {
    ASTNode attributes = ParseAttributeSections();
    Modifier mods      = ParseModifiers();

    ASTNode declNode = null;
    switch(Token.Type)
    {
      case TokenType.Event:
        ParseEventsDeclaration(attributes, mods, ref events);
        break;

      case TokenType.Class: case TokenType.Struct: case TokenType.Interface: // nested types of all kinds
      case TokenType.Enum: case TokenType.Delegate:
        if(containerType == TokenType.Interface) AddMessage(Diagnostic.NoTypesInInterfaces);
        declNode = ParseTypeDeclaration(attributes, mods);
        if(containerType != TokenType.Interface && declNode != null) ASTNode.AddSibling(ref types, declNode);
        break;
      
      default:
        if(Is(TokenType.Tilde) || Is(TokenType.Identifier) && NextIs(TokenType.LParen)) // destructor or constructor
        {
          if(containerType == TokenType.Interface)
          {
            AddMessage(Is(TokenType.Tilde) ? Diagnostic.NoDestructorOutsideClass
                                           : Diagnostic.NoConstructorInInterface);
          }

          declNode = ParseMethodDeclaration(attributes, mods);
          if(containerType != TokenType.Interface && declNode != null) ASTNode.AddSibling(ref methods, declNode);
        }
        else // it might be a method (other than constructor/destructor), field, or property.
        {
          TypeBase type;
          ParseType(out type);

          bool badDecl = type == null;
          if(!badDecl)
          {
            if(Is(TokenType.Identifier) && (NextIs(TokenType.Semicolon) || NextIs(TokenType.Equals))) // field
            {
              if(containerType == TokenType.Interface)
              {
                AddMessage(Diagnostic.NoFieldsInInterfaces);
                ASTNode dummy = null;
                ParseFieldsDeclaration(attributes, mods, type, ref dummy);
              }
              else ParseFieldsDeclaration(attributes, mods, type, ref fields);
            }
            else
            {
              Identifier name;
              ParseDottedName(out name);

              if(name == null)
              {
                badDecl = true;
              }
              else if(Is(TokenType.LCurly) || Is(TokenType.LSquare)) // property or indexer
              {
                declNode = ParsePropertyDeclaration(attributes, mods, type, name);
                if(declNode != null) ASTNode.AddSibling(ref properties, declNode);
              }
              else if(Is(TokenType.LParen)) // regular method
              {
                declNode = ParseMethodDeclaration(attributes, mods, type, name, false);
                if(declNode != null) ASTNode.AddSibling(ref methods, declNode);
              }
              else
              {
                badDecl = true;
              }
            }
          }

          if(badDecl)
          {
            AddMessage(Diagnostic.InvalidTokenInTypeDecl,
                       Enum.GetName(typeof(TokenType), Token.Type).ToLowerInvariant());
            RecoverFromBadDeclaration();
          }
        }
        break;
    }
  }

  /// <summary>Parses a delegate declaration.</summary>
  // DelegateDecl = delegate Type IDENT '(' ParamList ')' ;
  // EnumField = IDENT (= Expression)?
  TypeDeclarationNode ParseDelegateDeclaration(ASTNode attributes, Modifier modifiers)
  {
    throw new NotImplementedException();
    Eat(TokenType.Delegate);

    SetAttributeTargets(ref attributes, AttributeTarget.Method, AttributeTarget.Return);
  }

  /// <summary>Parses an enum declaration.</summary>
  // EnumDecl  = enum IDENT (: TYPE)? { (EnumField (, EnumField)* ,?)?- }
  // EnumField = IDENT (= Expression)?
  TypeDeclarationNode ParseEnumDeclaration(ASTNode attributes, Modifier modifiers)
  {
    throw new NotImplementedException();
    Eat(TokenType.Enum);

    SetAttributeTargets(ref attributes, AttributeTarget.Type);

    // parse the name
    Identifier name;
    if(!ParseIdentifier(out name) && name == null)
    {
      RecoverFromBadDeclaration();
      return null;
    }

    // check for and consume the underlying type if it was specified
    TypeBase underlyingType = null;
    if(TryEat(TokenType.Colon))
    {
      if(Token.Type < TokenType.EnumTypeStart || Token.Type >= TokenType.TypeEnd)
      {
        AddMessage(Diagnostic.ExpectedEnumType);
        RecoverTo(TokenType.LCurly);
      }
      else
      {
        underlyingType = GetBuiltinType(Token.Type);
        NextToken();
      }
    }

    if(!EatOpenCurly())
    {
      if(!RecoverTo(TokenType.LCurly)) return null;
      Eat(TokenType.LCurly);
    }
    
  }

  /// <summary>Parses an events declaration and adds the events to the given list.</summary>
  // EventDecl = event Type Dotted ((, Dotted)* ; | { AddMethod RemoveMethod })
  // AddMethod = AttributeSections? add { Statement* }
  // RemoveMethod = AttributeSections? remove { Statement* }
  ASTNode ParseEventsDeclaration(ASTNode attributes, Modifier mods, ref ASTNode eventList)
  {
    throw new NotImplementedException();
  }

  /// <summary>Parses a fields declaration, assuming the scanner is positioned after the field type.</summary>
  // FieldsDecl = FieldDecl (, FieldDecl)* ;
  // FieldDecl  = IDENT (= Expression)?
  void ParseFieldsDeclaration(ASTNode attributes, Modifier mods, TypeBase type, ref ASTNode fieldList)
  {
    throw new NotImplementedException();
  }

  /// <summary>Attempts to parse an identifier.</summary>
  /// <returns>True if an identifier was returned and a token consumed, and false otherwise.</returns>
  bool ParseIdentifier(out Identifier value)
  {
    string name;
    value = EatIdentifier(out name)
      ? new Identifier(name, previousToken.SourceName, previousToken.Start, previousToken.End) : null;
    return value != null;
  }

  /// <summary>Parses a method declaration.</summary>
  // GeneralMethodDecl = ConstructorDecl | DestructorDecl | MethodDecl
  // ConstructorDecl = IDENT '(' ParamList ')' (: (this|base) '(' ArgList ')')? { Statement* }
  // DestructorDecl  = ~ IDENT '(' ')' { Statement* }
  // MethodDecl      = Type Dotted '(' ParamList ')' { Statement* }
  ASTNode ParseMethodDeclaration(ASTNode attributes, Modifier mods)
  {
    TypeBase type = GetBuiltinType(TokenType.Void);
    Identifier name;
    bool isConstructor = false;

    if(TryEat(TokenType.Tilde)) // destructor
    {
      SetAttributeTargets(ref attributes, AttributeTarget.Method);
      ParseIdentifier(out name);
    }
    else if(Is(TokenType.Identifier) && NextIs(TokenType.LParen)) // constructor
    {
      SetAttributeTargets(ref attributes, AttributeTarget.Method);
      ParseIdentifier(out name);
      isConstructor = true;
    }
    else
    {
      SetAttributeTargets(ref attributes, AttributeTarget.Method, AttributeTarget.Return);
      ParseType(out type);
      ParseDottedName(out name);
    }

    if(name == null || type == null)
    {
      RecoverFromBadDeclaration();
      return null;
    }
    else
    {
      return ParseMethodDeclaration(attributes, mods, type, name, isConstructor);
    }
  }

  /// <summary>Parses a method declaration, assuming the scanner is positioned at the opening paranthesis.</summary>
  ASTNode ParseMethodDeclaration(ASTNode attributes, Modifier mods, TypeBase type, Identifier name, bool isConstructor)
  {
    throw new NotImplementedException();
  }

  Modifier ParseModifiers()
  {
    Modifier mods = new Modifier();
    while(IsModifierKeyword || IsIdentifier("partial"))
    {
      Modifier mod = Is(TokenType.Identifier) ? Modifier.Partial : TokenToModifier(Token.Type);
      if((mods & mod) != 0)
      {
        AddMessage(Diagnostic.DuplicateModifier, Enum.GetName(typeof(Modifier), mod).ToLowerInvariant());
      }
      mods &= mod;
    }
    return mods;
  }
  
  // a namespace contains extern aliases, followed by using statements, followed by type declarations
  NamespaceNode ParseNamespaceInterior(Identifier name)
  {
    NamespaceNode nsNode = new NamespaceNode(name);
    
    // first parse all the extern aliases
    List<string> list = null;
    while(TryEat(TokenType.Extern))
    {
      EnsureNoXmlComment();
      string aliasName;
      if(!EatPseudoKeyword("alias") || !EatIdentifier(out aliasName))
      {
        RecoverTo(TokenType.Semicolon);
      }
      else
      {
        AddItem(ref list, aliasName);
      }
      EatSemicolon();
    }
    nsNode.ExternAliases = ToArray(list);

    // then parse all the using statements. a using statement is 'using IDENT = DOTTED_IDENT;' or 'using DOTTED_IDENT;'
    while(TryEat(TokenType.Using))
    {
      EnsureNoXmlComment();
      FilePosition start = PreviousPosition;

      Identifier identifier;
      TypeBase typeRef = null;
      bool wasDotted;

      if(!ParseDottedName(out identifier, out wasDotted)) RecoverTo(TokenType.Semicolon);
      else if(!wasDotted && TryEat(TokenType.Equals)) // if the next token is '=', it looks like a using alias
      {
        if(!ParseTypeName(out typeRef)) RecoverTo(TokenType.Semicolon);
      }

      EatSemicolon();
      if(identifier.Name == null) continue; // if we failed to read at least the type/namespace name, skip this one

      UsingNode usingNode = typeRef == null ? (UsingNode)new UsingNamespaceNode(identifier)
                                            : new UsingAliasNode(identifier.Name, typeRef);
      nsNode.AddUsingNode(SetSpan(start, previousToken.End, usingNode));
    }

    // if this is the outermost namespace, allow global attributes
    if(name == null)
    {
      nsNode.GlobalAttributes = ParseGlobalAttributes();
    }

    // then parse all the type declarations
    while(CouldBeStartOfDeclaration() || Is(TokenType.Namespace))
    {
      if(TryEat(TokenType.Namespace))
      {
        Identifier dotted;
        ParseIdentifier(out dotted);
        EatOpenCurly();
        NamespaceNode nestedNs = ParseNamespaceInterior(dotted);
        EatClosingCurly();
        nsNode.AddNamespace(nestedNs);
      }
      else
      {
        TypeDeclarationNode declNode = ParseTypeDeclaration();
        if(declNode != null) nsNode.AddTypeDeclaration(declNode);
      }
    }

    return nsNode;
  }

  /// <summary>Parses a property declaration, assuming the scanner is positioned at the opening square bracket or
  /// curly brace.
  /// </summary>
  // PropertyDecl = ([ ParamList ])? { GetAccessor SetAccessor? | SetAccessor GetAccessor? }
  // GetAccessor = AttributeSections? Modifiers get { Statement* }
  // SetAccessor = AttributeSections? Modifiers set { Statement* }
  ASTNode ParsePropertyDeclaration(ASTNode attributes, Modifier mods, TypeBase type, Identifier name)
  {
    throw new NotImplementedException();
  }

  /// <summary>Parses a namespace or type name (minus any type parameters/arguments).</summary>
  // TypeOrNamespaceName = TYPEKEYWORD | (IDENT ::)? DOTTED
  bool ParseTypeOrNamespaceName(out TypeBase type)
  {
    bool success = true;

    if(IsTypeKeyword)
    {
      type = GetBuiltinType(Token.Type);
      NextToken();
    }
    else
    {
      type = null;

      Identifier scope = null;
      if(NextIs(TokenType.Scope))
      {
        success = ParseIdentifier(out scope) && success;
        TryEat(TokenType.Scope);
      }

      Identifier name;
      success = ParseDottedName(out name) && success;
      if(name != null)
      {
        if(scope != null) name.Scope = scope.Name;
        type = new UnresolvedType(name);
      }
    }

    return success;
  }
  
  /// <summary>Parses an optional set of type arguments.</summary>
  /// <param name="type">The type that will be converted to a generic type if type arguments are found.</param>
  /// <param name="allowOpenType">Whether open generic types are allowed.</param>
  /// <returns>False if an error occurred.</returns>
  // TypeArgs = < Type (, Type)* >
  bool ParseTypeArgs(ref TypeBase type, bool allowOpenType)
  {
    bool success = true;

    if(TryEat(TokenType.LessOrEq)) // if there are any type arguments
    {
      throw new NotImplementedException();
      
      Position start = Token.Start; // save the position for error reporting
      // there are two cases -- open or closed. open types have no type arguments, eg G<,>. closed types do.
      // also, in the case of nested generic types, eg A<B<C>>, we need to terminate the loop on receiving
      // >, >>. then, if it was >>, we need to convert the token to >
      List<TypeBase> typeArgs = null;
      int arity = 1;

      if(Is(TokenType.Comma)) // if it starts with a comma, it's an open type with arity > 1
      {
        do
        {
          Eat(TokenType.Comma);
          arity++;
        } while(Is(TokenType.Comma));
      }
      else // otherwise, it's either a closed type or an open type with arity == 1
      {
        while(!Is(TokenType.GreaterThan) && !Is(TokenType.RShift))
        {
          // it's an error if there's a comma before the first type arg, or no comma after the first
          if(TryEat(TokenType.Comma) ^ typeArgs == null) success = false;

          TypeBase typeArg;
          success = ParseType(out typeArg) && success;
          // if the type was null, we may not have consumed any tokens, so break to prevent an infinite loop
          if(typeArg == null) break;
          AddItem(ref typeArgs, typeArg);
        }
      }

      if(Is(TokenType.RShift)) // if it's >>, convert it to >
      {
        Token.Type = TokenType.GreaterThan;
      }
      else if(!Is(TokenType.GreaterThan)) // otherwise, if it's not >, we've got an error. try to recover.
      {
        AddMessage(Diagnostic.ExpectedCharacter, '>');
        RecoverPast(TokenType.Identifier, TokenType.Period, TokenType.Comma, TokenType.RShift, TokenType.GreaterThan);
        success = false;
      }

      if(!allowOpenType && typeArgs == null) // if it's an open type and that's not allowed,
      {                                        // given an error and don't convert the type
        AddMessage(Diagnostic.ExpectedIdentifier, start);
        success = false;
      }
      else
      {
        throw new NotImplementedException();
        //type = typeArgs == null ? (GenericTypeBase)new OpenType(type, arity)
        //                          : new ConstructedType(type, ToArray(typeArgs));
      }
    }

    return success;
  }
  
  
  /// <summary>Parses a type-name (defined at ecma334-10.8).</summary>
  /// <param name="type">A variable that receives the type. This may be valid even in the case of an error.</param>
  // TypeName = NamespaceName TypeArgs? (. IDENT TypeArgs?)*
  bool ParseTypeName(out TypeBase type)
  {
    bool success = ParseTypeOrNamespaceName(out type);
    if(type != null)
    {
      success = ParseTypeArgs(ref type, false) && success;

      while(TryEat(TokenType.Period)) // we should only get into this loop if there were type arguments
      {
        Identifier ident;
        success = ParseIdentifier(out ident) && success;
        if(ident == null) break;
        type = new UnresolvedNestedType(type, ident);
      }
    }
    return success;
  }

  /// <summary>Parses a type (defined at ecma334-A.2.2).</summary>
  /// <param name="type">A variable that receives the type. This may be valid even in the case of an error.</param>
  // Type = TypeName '?'? '*'* [,*]?
  bool ParseType(out TypeBase type)
  {
    bool success = ParseTypeName(out type);
    if(type != null)
    {
      if(TryEat(TokenType.Question))
      {
        type = new NullableType(type);
      }

      while(TryEat(TokenType.Asterisk)) type = new PointerType(type);

      if(TryEat(TokenType.LSquare))
      {
        int rank = 1;
        while(TryEat(TokenType.Comma)) rank++;

        if(!TryEat(TokenType.RSquare))
        {
          AddMessage(Diagnostic.ExpectedCharacter, previousToken.End, ']');
          success = false;
        }

        type = new ArrayType(type, rank);
      }
    }

    return success;
  }

  /// <summary>Parses a type declaration (class, struct, enum, or delegate).</summary>
  TypeDeclarationNode ParseTypeDeclaration()
  {
    ASTNode attributes = ParseAttributeSections();
    Modifier mods      = ParseModifiers();
    return ParseTypeDeclaration(attributes, mods);
  }

  /// <summary>Parses a type declaration, assuming that we're positioned at the identifying keyword.</summary>
  TypeDeclarationNode ParseTypeDeclaration(ASTNode attributes, Modifier mods)
  {
    switch(Token.Type)
    {
      case TokenType.Class: case TokenType.Struct: case TokenType.Interface:
        return ParseClassDeclaration(attributes, mods, Token.Type);
      case TokenType.Enum:
        return ParseEnumDeclaration(attributes, mods);
      case TokenType.Delegate:
        return ParseDelegateDeclaration(attributes, mods);
      default:
        AddMessage(Diagnostic.ExpectedTypeDeclaration);
        RecoverFromBadDeclaration();
        return null;
    }
  }

  /// <summary>Advances to the next token and returns its type.</summary>
  new TokenType NextToken()
  {
    previousToken = Token;
    base.NextToken();
    return Token.Type;
  }

  /// <summary>Attempts to recover from a malformed declaration. This should not be called in the interior of the
  /// declaration -- after the opening brace.
  /// </summary>
  void RecoverFromBadDeclaration()
  {
    TokenType type = RecoverTo(TokenType.LCurly, TokenType.RCurly, TokenType.Semicolon);
    if(type == TokenType.LCurly) SkipPastBlock(0);
    else if(type == TokenType.RCurly || type == TokenType.Semicolon) NextToken();
  }

  /// <summary>Recover by skipping past all the types listed.</summary>
  void RecoverPast(params TokenType[] types)
  {
    while(Is(types)) NextToken();
  }

  /// <summary>Attempts to skip tokens until it finds a token of the given type. This method will not skip past
  /// <see cref="TokenType.EOF"/>.
  /// </summary>
  /// <returns>Returns true if it stopped on a token of type <paramref name="type"/>, and false otherwise.</returns>
  bool RecoverTo(TokenType type)
  {
    while(!Is(type) && !Is(TokenType.EOF) && !Is(TokenType.EOD)) NextToken();
    return Is(type);
  }

  /// <summary>Attempts to skip tokens until it finds a token matching one of the given types. This method will not
  /// skip past <see cref="TokenType.EOF"/>.
  /// </summary>
  /// <returns>Returns the type of the token it stopped on.</returns>
  TokenType RecoverTo(params TokenType[] types)
  {
    while(!Is(types) && !Is(TokenType.EOF) && !Is(TokenType.EOD)) NextToken();
    return Token.Type;
  }

  /// <summary>Given a linked list of attribute nodes, validates and sets their targets. If an attribute node has no
  /// target, it will be set to the first valid target. The linked list may be modified to remove attributes with
  /// invalid targets.
  /// </summary>
  void SetAttributeTargets(ref ASTNode attributeNodes, params AttributeTarget[] validTargets)
  {
    Debug.Assert(validTargets.Length > 0);

    ASTNode previousNode = null;
    foreach(AttributeNode node in ASTNode.Enumerate(attributeNodes))
    {
      if(node.Target == AttributeTarget.Unknown) // if the target is unspecified, apply the default target
      {
        node.Target = validTargets[0];
      }
      else if(Array.IndexOf(validTargets, node.Target) == -1) // otherwise, the target was specified but invalid
      {
        // construct the warning message about the target being invalid
        string location = Enum.GetName(typeof(AttributeTarget), node.Target).ToLowerInvariant();
        string validLocations = null;
        foreach(AttributeTarget target in validTargets)
        {
          if(validLocations != null) validLocations += ", ";
          validLocations += Enum.GetName(typeof(AttributeTarget), target).ToLowerInvariant();
        }
        AddMessage(Diagnostic.UnknownAttributeTarget, node.Start, location, validLocations);

        // remove 'node' from the linked list
        if(previousNode == null) attributeNodes = node.Next;
        else previousNode.Next = node.Next;
      }
    }
  }

  /// <summary>Skips a number of closing curly braces.</summary>
  /// <param name="initialDepth">The initial depth within the block. For each open curly brace, the depth is
  /// incremented. For each closing curly brace, it's decremented. If the number after being decremented is less than
  /// or equal to zero, the method returns. Therefore, greater values of <paramref name="initialDepth"/> cause it to
  /// skip more closing curly braces before returning.
  /// </param>
  void SkipPastBlock(int initialDepth)
  {
    while((!TryEat(TokenType.RCurly) || --initialDepth > 0) && Is(TokenType.EOF) && Is(TokenType.EOD))
    {
      if(TryEat(TokenType.LCurly)) initialDepth++;
    }
  }

  /// <summary>Attempts to consume a token of the given type. No diagnostics are reported if the current token is not
  /// of the given type.
  /// </summary>
  /// <returns>True if the token was consumed, and false if not.</returns>
  bool TryEat(TokenType type)
  {
    if(Is(type))
    {
      NextToken();
      return true;
    }
    else return false;
  }

  Token previousToken;

  static TypeBase GetBuiltinType(TokenType type)
  {
    throw new NotImplementedException();
    // FIXME: these should not allocate new objects each time
    switch(type)
    {
      case TokenType.Bool:    return DotNetType.Get(typeof(bool));
      case TokenType.Byte:    return DotNetType.Get(typeof(byte));
      case TokenType.Char:    return DotNetType.Get(typeof(char));
      case TokenType.Decimal: return DotNetType.Get(typeof(decimal));
      case TokenType.Double:  return DotNetType.Get(typeof(double));
      case TokenType.Float:   return DotNetType.Get(typeof(float));
      case TokenType.Int:     return DotNetType.Get(typeof(int));
      case TokenType.Long:    return DotNetType.Get(typeof(long));
      case TokenType.Object:  return DotNetType.Get(typeof(object));
      case TokenType.Sbyte:   return DotNetType.Get(typeof(sbyte));
      case TokenType.Short:   return DotNetType.Get(typeof(short));
      case TokenType.String:  return DotNetType.Get(typeof(string));
      case TokenType.Uint:    return DotNetType.Get(typeof(uint));
      case TokenType.Ulong:   return DotNetType.Get(typeof(ulong));
      case TokenType.Ushort:  return DotNetType.Get(typeof(ushort));
      case TokenType.Void:    return DotNetType.Get(typeof(void));
      default: throw new ArgumentException();
    }
  }

  static Modifier TokenToModifier(TokenType type)
  {
    Debug.Assert(type >= TokenType.ModifierStart && type < TokenType.ModifierEnd);
    return (Modifier)(1<<(int)(type-TokenType.ModifierStart));
  }

  static T SetSpan<T>(string sourceName, T node) where T : ASTNode
  {
    node.SourceName = sourceName;
    return node;
  }

  static T SetSpan<T>(FilePosition start, Position end, T node) where T : ASTNode
  {
    node.SetSpan(start.SourceName, start.Position, end);
    return node;
  }
}
#endregion

} // namespace Scripting.CSharper