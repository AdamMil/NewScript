using System;
using System.Collections.Generic;
using Scripting.AST;

namespace Scripting.CSharper
{

#region Compiler
/// <summary>This class represents the CSharper (C#er) compiler.</summary>
public class Compiler : Scripting.AST.Compiler<CompilerOptions>
{
  /// <summary>The number of items we can store in the options stack before it goes bust.</summary>
  const int StackCapacity = 4;

  /// <summary>
  /// Pushes a new set of compiler options, with values inherited from the current set, onto the options stack.
  /// </summary>
  public void PushOptions()
  {
    if(optionDepth == StackCapacity) throw new InvalidOperationException("The option stack is full.");
    optionDepth++;
    options = new CompilerOptions(options);
  }

  /// <summary>Pops the topmost set of compiler options from the options stack.</summary>
  public void PopOptions()
  {
    if(optionDepth == 0) throw new InvalidOperationException("The option stack is empty.");
    optionDepth--;
    options = options.Parent;
  }

  /// <summary>How many <see cref="CompilerOptions"/> have been pushed onto the option stack.</summary>
  int optionDepth;
}
#endregion

#region CompilerOptions
/// <summary>This class represents the compiler options for the C#er language.</summary>
public class CompilerOptions : Scripting.AST.CompilerOptions
{
  /// <summary>Initializes the class with a default set of compiler options.</summary>
  public CompilerOptions() { }

  /// <summary>Initializes the class with a set of compiler options inherited from the given
  /// <see cref="CompilerOptions"/> object.
  /// </summary>
  public CompilerOptions(CompilerOptions parent)
  {
    parent.CloneTo(this);
    this.parent = parent;
  }

  /// <summary>The previous set of options in the options stack. This will be null for the topmost set of options.</summary>
  public CompilerOptions Parent
  {
    get { return parent; }
  }

  /// <summary>The warning level. All warnings with a level greater than this are not shown.</summary>
  public int WarningLevel = int.MaxValue;
  /// <summary>Whether warnings are treated as errors.</summary>
  public bool TreatWarningsAsErrors;

  /// <summary>Causes a warning to be disabled, so it won't be shown.</summary>
  public void DisableWarning(int code)
  {
    // if all warnings are disabled, we're using the 'warnings' list to hold those that were explicitly restored
    if(allWarningsDisabled) RemoveFromWarnings(code);
    else AddToWarnings(code);
  }

  /// <summary>Determines whether the given warning has been disabled. This will check the parent as well.</summary>
  public bool IsWarningDisabled(int code)
  {
    bool isInList = warnings != null && warnings.BinarySearch(code) >= 0;
    if(allWarningsDisabled && !isInList || !allWarningsDisabled && isInList) return true;
    else return parent == null ? false : parent.IsWarningDisabled(code);
  }

  /// <summary>Reenables the given warning. Note that if the warning is disabled by the parent, it will still be
  /// effectively disabled.
  /// </summary>
  public void RestoreWarning(int code)
  {
    // if all warnings are disabled, we're using the 'warnings' list to hold those that were explicitly restored
    if(allWarningsDisabled) AddToWarnings(code);
    else RemoveFromWarnings(code);
  }

  /// <summary>Disables all warnings.</summary>
  public void DisableWarnings()
  {
    warnings = null;
    allWarningsDisabled = true;
  }

  /// <summary>Restores all warnings. Note that warnings disabled by the parent will still be disabled.</summary>
  public void RestoreWarnings()
  {
    warnings = null;
    allWarningsDisabled = false;
  }

  /// <summary>Whether the diagnostic should be shown. That is, whether it's not disabled and passes the warning level.</summary>
  public bool ShouldShow(Diagnostic diagnostic)
  {
    return diagnostic.Type != OutputMessageType.Warning ||
           diagnostic.Level <= WarningLevel && !IsWarningDisabled(diagnostic.Code);
  }

  /// <summary>Defines the given preprocessor symbol.</summary>
  public void Define(string identifier)
  {
    if(defines == null) defines = new Dictionary<string,bool>();
    defines[identifier] = true;
  }

  /// <summary>Checks whether the given preprocessor symbol has been defined.</summary>
  public bool IsDefined(string identifier)
  {
    bool defined;
    if(defines != null && defines.TryGetValue(identifier, out defined)) return defined;
    else return parent == null ? false : parent.IsDefined(identifier);
  }

  /// <summary>
  /// Undefines the given preprocessor symbol. The symbol will be undefined even if the parent still defines it.
  /// </summary>
  public void Undefine(string identifier)
  {
    if(defines == null) defines = new Dictionary<string,bool>();
    defines[identifier] = false;
  }

  void CloneTo(CompilerOptions options)
  {
    base.CloneTo(options);
    options.WarningLevel = WarningLevel;
    options.TreatWarningsAsErrors = TreatWarningsAsErrors;
  }

  /// <summary>Adds the given code to the warnings list.</summary>
  void AddToWarnings(int code)
  {
    if(warnings == null) warnings = new List<int>();
    int index = warnings.BinarySearch(code);
    if(index < 0) warnings.Insert(~index, code);
  }

  /// <summary>Removes the given code from the warnings list.</summary>
  void RemoveFromWarnings(int code)
  {
    if(warnings != null)
    {
      int index = warnings.BinarySearch(code);
      if(index >= 0) warnings.RemoveAt(index);
    }
  }

  readonly CompilerOptions parent;
  /// <summary>A dictionary that holds values indicating whether a given preprocessor definition has been explicitly
  /// defined or undefined.
  /// </summary>
  Dictionary<string,bool> defines;
  /// <summary>Holds either warnings that are disabled (if !allWarningsDisabled) or warnings that are enabled (if
  /// allWarningsDisabled).</summary>
  List<int> warnings;
  /// <summary>Indicates whether all warnings are disabled by default.</summary>
  bool allWarningsDisabled;
}
#endregion

} // namespace Scripting.CSharper