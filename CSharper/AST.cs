using System;
using Scripting.AST;

namespace Scripting.CSharper
{

#region AttributeTarget
public enum AttributeTarget
{
  Unknown, Assembly, Event, Field, Method, Param, Property, Return, Type, TypeVar
}
#endregion

#region Modifier
/// <summary>The modifiers for a type or member declaration.</summary>
public enum Modifier
{
  #pragma warning disable 1591
  // these must be in the same order as in TokenType
  None, Abstract=0x1, Const=0x2, Explicit=0x4, Extern=0x8, Fixed=0x10, Implicit=0x20, Internal=0x40, New=0x80,
  Override=0x100, Private=0x200, Protected=0x400, Public=0x800, ReadOnly=0x1000, Sealed=0x2000, Static=0x4000,
  Unsafe=0x8000, Virtual=0x10000, Volatile=0x20000,
  // these are not contained in TokenType (they're pseudo-keywords)
  Partial=0x40000,
  #pragma warning restore 1591
}
#endregion

public enum TypeType { Class, Struct, Interface }

#region AttributeNode
public class AttributeNode : ConstructorCallNode
{
  public AttributeNode(AttributeTarget target, TypeBase type,
                       ASTNode[] arguments, string[] names, ASTNode[] namedArguments)
    : base(type, arguments, names, namedArguments)
  {
    Target = target;
  }

  public AttributeTarget Target;
}
#endregion

#region ConstructorCallNode
public abstract class ConstructorCallNode : ASTNode
{
  public ConstructorCallNode(TypeBase type, ASTNode[] arguments, string[] names, ASTNode[] namedArguments)
  {
    if(type == null) throw new ArgumentNullException();
    if(names == null ^ namedArguments == null || names != null && names.Length != namedArguments.Length)
    {
      throw new ArgumentException();
    }

    this.type           = type;
    this.arguments      = arguments;
    this.names          = names;
    this.namedArguments = namedArguments;
  }

  TypeBase type;
  string[] names;
  ASTNode[] arguments, namedArguments;
}
#endregion

#region NamespaceNode
/// <summary>This node represents a namespace declaration. A namespace declaration contains using statements and type
/// declarations.
/// </summary>
public class NamespaceNode : ASTNode
{
  /// <summary>Initializes this namespace with the given, possibly-null, name.</summary>
  public NamespaceNode(Identifier name)
  {
    Name = name.Name;
    SetSpan(name.Span);
  }

  /// <summary>This namespace's external aliases.</summary>
  public string[] ExternAliases;
  /// <summary>A linked list of the using statements within this namespace.</summary>
  public ASTNode UsingNodes;
  /// <summary>A linked list of the namespaces nested within this namespace.</summary>
  public ASTNode Namespaces;
  /// <summary>A linked list of the types contained within this namespace.</summary>
  public ASTNode Types;
  /// <summary>A linked list of global assembly attributes.</summary>
  public ASTNode GlobalAttributes;
  /// <summary>The possibly-null name of the namespace.</summary>
  public readonly string Name;

  /// <summary>Adds a namespace node to <see cref="Namespaces"/>.</summary>
  public void AddNamespace(NamespaceNode node)
  {
    ASTNode.AddSibling(ref Namespaces, node);
  }

  /// <summary>Adds a type declaration to <see cref="Types"/>.</summary>
  public void AddTypeDeclaration(TypeDeclarationNode node)
  {
    ASTNode.AddSibling(ref Types, node);
  }

  /// <summary>Adds a using node to <see cref="UsingNodes"/>.</summary>
  public void AddUsingNode(UsingNode node)
  {
    ASTNode.AddSibling(ref UsingNodes, node);
  }
}
#endregion

#region SourceFileNode
/// <summary>This class represents a single source file.</summary>
public class SourceFileNode : ASTNode
{
  /// <summary>Initializes this node with the root namespace of a file.</summary>
  public SourceFileNode(NamespaceNode root)
  {
    if(root == null) throw new ArgumentNullException();
    Root = root;
  }

  /// <summary>The root namespace of a file.</summary>
  public readonly NamespaceNode Root;
}
#endregion

#region TypeDeclarationNode
public class TypeDeclarationNode : ASTNode
{
  public TypeDeclarationNode(Identifier name, TypeType type)
  {
    this.Type = type;
    this.name = name;
    SetSpan(name.Span);
  }

  public ASTNode Events, Fields, Methods, Properties, Types;
  public readonly TypeType Type;

  readonly Identifier name;
}
#endregion

#region UsingNode
/// <summary>This node represents a 'using' statement in a namespace.</summary>
public abstract class UsingNode : ASTNode
{
}
#endregion

#region UsingNamespaceNode
public class UsingNamespaceNode : UsingNode
{
  public UsingNamespaceNode(Identifier nsName)
  {
    name = nsName;
  }

  readonly Identifier name;
}
#endregion

#region UsingAliasNode
public class UsingAliasNode : UsingNode
{
  public UsingAliasNode(string alias, TypeBase type)
  {
    this.Alias = alias;
    this.type  = type;
  }

  public readonly string Alias;

  public TypeBase Type
  {
    get { return type; }
  }

  TypeBase type;
}
#endregion

} // namespace Scripting.CSharper