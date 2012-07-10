using System;
using Scripting.AST;

namespace Scripting.CSharper
{

  public class ExpressionNode : ASTNode
  {
  }

  public class AssignNode : ExpressionNode
  {
    public AssignNode(ExpressionNode lhs, ExpressionNode rhs, bool opAssignment)
    {
      if(lhs == null || rhs == null) throw new ArgumentNullException();
      LHS = lhs;
      RHS = rhs;
      IsOpAssignment = opAssignment;
    }

    public readonly ExpressionNode LHS, RHS;
    public readonly bool IsOpAssignment;
  }

  public class IdentifierNode : ExpressionNode
  {
    public IdentifierNode(string name)
    {
      if(name == null) throw new ArgumentNullException();
      Name = name;
    }

    public readonly string Name;
  }

} // namespace Scripting.CSharper