using System;
using System.Text;

namespace Scripting.CSharper
{

public abstract class TypeBase
{
  public sealed override string ToString()
  {
    StringBuilder sb = new StringBuilder();
    ToString(sb);
    return sb.ToString();
  }

  protected internal abstract void ToString(StringBuilder sb);
}

public abstract class AggregateType : TypeBase
{
  public AggregateType(TypeBase elementType)
  {
    if(elementType == null) throw new ArgumentNullException();
    if(elementType is ReferenceType) throw new ArgumentException("Cannot aggregate a reference type.");
    this.elementType = elementType;
  }

  public TypeBase ElementType
  {
    get { return elementType; }
  }

  TypeBase elementType;
}

public class ArrayType : AggregateType
{
  public ArrayType(TypeBase elementType, int rank) : base(elementType)
  {
    if(rank < 1) throw new ArgumentOutOfRangeException();
    this.rank = rank;
  }

  protected internal override void ToString(StringBuilder sb)
  {
    ElementType.ToString(sb);
    sb.Append('[').Append(',', rank-1).Append(']');
  }

  readonly int rank;
}

public class DotNetType : TypeBase
{
  DotNetType(Type type)
  {
    Type = type;
  }

  public readonly Type Type;

  public static TypeBase Get(Type type)
  {
    if(type == null) throw new ArgumentNullException();

    // strip away array, pointer, and byref, etc with our own type wrappers
    if(type.IsArray) return new ArrayType(Get(type.GetElementType()), type.GetArrayRank());
    else if(type.IsPointer) return new PointerType(Get(type.GetElementType()));
    else if(type.IsByRef) return new ReferenceType(Get(type.GetElementType()));
    else if(type.IsGenericType) throw new NotImplementedException();
    else return new DotNetType(type);
  }

  protected internal override void ToString(StringBuilder sb)
  {
    sb.Append(Diagnostic.TypeName(Type));
  }
}

public class NullableType : AggregateType
{
  public NullableType(TypeBase elementType) : base(elementType)
  {
    if(elementType is NullableType) throw new ArgumentException("Can't have a doubly-nullable type.");
  }

  protected internal override void ToString(StringBuilder sb)
  {
    ElementType.ToString(sb);
    sb.Append('?');
  }
}

public class PointerType : AggregateType
{
  public PointerType(TypeBase elementType) : base(elementType) { }

  protected internal override void ToString(StringBuilder sb)
  {
    ElementType.ToString(sb);
    sb.Append('*');
  }
}

public class ReferenceType : AggregateType
{
  public ReferenceType(TypeBase elementType) : base(elementType) { }

  protected internal override void ToString(StringBuilder sb)
  {
    ElementType.ToString(sb);
    sb.Append('&');
  }
}

/*public abstract class GenericTypeBase : TypeBase
{
  public GenericTypeBase(TypeBase type)
  {
    if(type == null) throw new ArgumentNullException();
    this.type = type;
  }
  
  public TypeBase Type
  {
    get { return type; }
  }

  public abstract int Arity { get; }

  TypeBase type;
}

public class OpenType : GenericTypeBase
{
  public OpenType(TypeBase type, int arity) : base(type)
  {
    if(arity < 1) throw new ArgumentOutOfRangeException();
    this.arity = arity;
  }

  public override int Arity
  {
    get { return arity; }
  }

  protected internal override void ToString(StringBuilder sb)
  {
    Type.ToString(sb);
    sb.Append('<').Append(',', arity-1).Append('>');
  }

  readonly int arity;
}

public class ConstructedType : GenericTypeBase
{
  public ConstructedType(TypeBase type, TypeBase[] typeParameters) : base(type)
  {
    if(typeParameters == null) throw new ArgumentNullException();
    this.typeParameters = typeParameters;
  }

  public override int Arity
  {
    get { return typeParameters.Length; }
  }

  protected internal override void ToString(StringBuilder sb)
  {
    Type.ToString(sb);
    sb.Append('<');
    for(int i=0; i<typeParameters.Length; i++)
    {
      if(i != 0) sb.Append(',');
      typeParameters[i].ToString(sb);
    }
  }

  readonly TypeBase[] typeParameters;
}*/

public class UnresolvedType : TypeBase
{
  public UnresolvedType(Identifier name)
  {
    if(name == null) throw new ArgumentNullException();
    Name = name;
  }

  public readonly Identifier Name;

  protected internal override void ToString(StringBuilder sb)
  {
    sb.Append(Name);
  }
}

public class UnresolvedNestedType : UnresolvedType
{
  public UnresolvedNestedType(TypeBase type, Identifier name) : base(name)
  {
    if(type == null) throw new ArgumentNullException();
  }

  public readonly TypeBase Type;

  protected internal override void ToString(StringBuilder sb)
  {
    Type.ToString(sb);
    sb.Append('.').Append(Name);
  }
}

} // namespace Scripting.CSharper