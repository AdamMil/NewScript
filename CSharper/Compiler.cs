using System;
using System.Collections.Generic;
using Scripting.AST;

namespace Scripting.CSharper
{

#region Compiler
public class Compiler : Scripting.AST.Compiler<CompilerOptions>
{
  /// <summary>The number of items we can store in the options stack before it goes bust.</summary>
  const int StackCapacity = 4;

  public CompilerOptions PushOptions()
  {
    if(optionDepth == StackCapacity) throw new InvalidOperationException("The option stack is full.");
    optionDepth++;
    return options = new CompilerOptions(options);
  }

  public CompilerOptions PopOptions()
  {
    if(optionDepth == 0) throw new InvalidOperationException("The option stack is empty.");
    optionDepth--;
    return options = options.Parent;
  }

  int optionDepth;
}
#endregion

#region CompilerOptions
public class CompilerOptions : Scripting.AST.CompilerOptions
{
  public CompilerOptions() { }

  public CompilerOptions(CompilerOptions parent)
  {
    parent.CloneTo(this);
    this.parent = parent;
  }

  public CompilerOptions Parent
  {
    get { return parent; }
  }
  
  public void DisableWarning(int code)
  {
    if(warnings == null) warnings = new List<int>();
    int index = warnings.BinarySearch(code);
    if(index < 0) warnings.Insert(~index, code);
  }

  public bool IsWarningDisabled(int code)
  {
    if(warnings != null && warnings.BinarySearch(code) >= 0) return true;
    else return parent == null ? false : parent.IsWarningDisabled(code);
  }

  public void RestoreWarning(int code)
  {
    if(warnings != null)
    {
      int index = warnings.BinarySearch(code);
      if(index >= 0) warnings.RemoveAt(index);
    }
  }

  public void Define(string identifier)
  {
    if(defines == null) defines = new Dictionary<string,bool>();
    defines[identifier] = true;
  }

  public bool IsDefined(string identifier)
  {
    bool defined;
    if(defines != null && defines.TryGetValue(identifier, out defined)) return defined;
    else return parent == null ? false : parent.IsDefined(identifier);
  }

  public void Undefine(string identifier)
  {
    if(defines == null) defines = new Dictionary<string,bool>();
    defines[identifier] = false;
  }

  readonly CompilerOptions parent;
  Dictionary<string,bool> defines;
  List<int> warnings;
}
#endregion

} // namespace Scripting.CSharper