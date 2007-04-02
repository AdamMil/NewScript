using System;
using Scripting.AST;

namespace Scripting.CSharper
{

/// <summary>This class represents a single source file.</summary>
public class SourceFileNode : ASTNode
{
  public SourceFileNode(NamespaceNode root)
  {
    if(root == null) throw new ArgumentNullException();
    Root = root;
  }

  public readonly NamespaceNode Root;
}

/// <summary>This node represents a namespace declaration. A namespace declaration contains using statements and type
/// declarations.
/// </summary>
public class NamespaceNode : ASTNode
{
  public NamespaceNode(string name)
  {
    Name = name;
  }

  public string[] ExternAliases;
  public ASTNode UsingNodes;
  public readonly string Name;

  public void AddUsingNode(UsingNode node)
  {
    ASTNode.AddSibling(ref UsingNodes, node);
  }
}

/// <summary>This node represents a 'using' statement in a namespace.</summary>
public class UsingNode : ASTNode
{
  public UsingNode(string typeOrNs, string alias)
  {
    if(typeOrNs == null) throw new ArgumentNullException();
    Resource = typeOrNs;
    Alias    = alias;
  }

  readonly public string Resource, Alias;
}

} // namespace Scripting.CSharper