using System;
using System.Collections.Generic;

namespace Scripting.AST
{

/// <summary>This is the base class for all syntax tree nodes.</summary>
public abstract class ASTNode
{
  /// <summary>Gets or sets next sibling of this node. If this node has no next sibling, this will be null.</summary>
  public ASTNode Next
  {
    get { return next; }
    set { next = value; }
  }

  /// <summary>The name of the source to which this node corresponds.</summary>
  public string SourceName;
  /// <summary>The span within the source corresponding to this node.</summary>
  public Position Start, End;

  /// <summary>Adds a list of items to the end of the sibling list for this node.</summary>
  /// <param name="itemList">A non-null list of items to add to the <see cref="Next"/> list for this node.</param>
  public void AddSibling(ASTNode itemList)
  {
    AddSibling(ref next, itemList);
  }

  /// <summary>Sets the <see cref="SourceName"/>, <see cref="Start"/>, and <see cref="End"/> fields to the given
  /// values.
  /// </summary>
  public void SetSpan(string sourceName, Position start, Position end)
  {
    this.sourceName = sourceName;
    this.start      = start;
    this.end        = end;
  }

  /// <summary>Sets the <see cref="SourceName"/>, <see cref="Start"/>, and <see cref="End"/> fields to span.</summary>
  public void SetSpan(FileSpan span)
  {
    this.sourceName = span.SourceName;
    this.start      = span.Span.Start;
    this.end        = span.Span.End;
  }

  /// <summary>Adds the given items to the end of the linked list formed by <paramref name="node"/>. If
  /// <paramref name="node"/> is null, it will be set to <paramref name="itemList"/>.
  /// </summary>
  /// <param name="node">A reference to the linked list to which the items will be added.</param>
  /// <param name="itemList">A non-null linked list of items (possibly just a single item) to add to
  /// <paramref name="node"/>.
  /// </param>
  public static void AddSibling(ref ASTNode node, ASTNode itemList)
  {
    if(itemList == null) throw new ArgumentNullException();
    if(node == null) node = itemList;
    else node.FindLastSibling().Next = itemList;
  }

  /// <summary>Enumerates all of the nodes in the given list, which can be null.</summary>
  public static IEnumerable<ASTNode> Enumerate(ASTNode list)
  {
    while(list != null)
    {
      yield return list;
      list = list.next;
    }
  }

  /// <summary>Finds the last node in the list. If there are no siblings beyond this one, this node will be returned.</summary>
  ASTNode FindLastSibling()
  {
    ASTNode current = this;
    while(current.next != null) current = current.next;
    return current;
  }

  ASTNode next;
  string sourceName;
  Position start, end;
}

} // namespace Scripting.AST