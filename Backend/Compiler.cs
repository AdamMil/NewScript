using System;
using System.Collections.ObjectModel;

namespace Scripting.AST
{

/// <summary>The base class of all compilers. This class cannot be used directly. Instead, use
/// <see cref="Compiler{T}"/>.
/// </summary>
public abstract class CompilerBase
{
  internal CompilerBase() { }

  /// <summary>Gets whether any errors have been reported.</summary>
  public bool HasErrors
  {
    get { return Messages.HasErrors; }
  }

  /// <summary>A collection of messages from the compiler.</summary>
  public readonly OutputMessageCollection Messages = new OutputMessageCollection();
}

/// <summary>The base class for all user-defined compilers, although it can be used as-is. The class represents an
/// instance of the compiler and its associated data, such as options.</summary>
/// <typeparam name="OptionType">The type of compiler options that the compiler will have.</typeparam>
public class Compiler<OptionType> : CompilerBase where OptionType : CompilerOptions, new()
{
  /// <summary>The compiler's options.</summary>
  public OptionType Options
  {
    get { return options; }
  }

  /// <summary>A field holding the current set of compiler options.</summary>
  protected OptionType options = new OptionType();
}

/// <summary>The base class of all user-defined sets of compiler options.</summary>
public class CompilerOptions
{
  /// <summary>Whether the compiler should generate optimized code.</summary>
  public bool Optimized;
  /// <summary>Whether the compiler should generate debug symbols.</summary>
  public bool Debug;

  /// <summary>Copies the options from this instance to another.</summary>
  protected void CloneTo(CompilerOptions options)
  {
    options.Optimized = Optimized;
    options.Debug = Debug;
  }
}

/// <summary>Defines a base class for classes that contain an instance of a <see cref="Compiler"/>. It exposes a
/// the compiler instance and contains a few methods to add messages to the compiler. This class is not particularly
/// intended for use outside this assembly, but it is available.
/// </summary>
public abstract class CompilerUserBase<CompilerType> where CompilerType : CompilerBase
{
  /// <summary>Initializes the class with the given compiler reference, which cannot be null.</summary>
  protected CompilerUserBase(CompilerType compiler)
  {
    if(compiler == null) throw new ArgumentNullException("compiler");
    this.compiler = compiler;
  }

  /// <summary>Gets the compiler passed to the constructor.</summary>
  protected CompilerType Compiler
  {
    get { return compiler; }
  }

  /// <summary>Adds an output message to <see cref="Compiler"/>.</summary>
  protected void AddMessage(OutputMessage message)
  {
    compiler.Messages.Add(message);
  }

  /// <summary>Adds a new error message to <see cref="Compiler"/> using the given source name and position.</summary>
  protected void AddErrorMessage(string sourceName, Position position, string message)
  {
    AddMessage(new OutputMessage(OutputMessageType.Error, message, sourceName, position));
  }

  readonly CompilerType compiler;
}

#region OutputMessage, OutputMessageType, and OutputMessageCollection
/// <summary>The type of an output message from the compiler.</summary>
public enum OutputMessageType
{
  /// <summary>The message is for informative purposes only.</summary>
  Information,
  /// <summary>The message indicates a non-fatal problem.</summary>
  Warning,
  /// <summary>The message indicates a fatal error, which will prevent compilation.</summary>
  Error
}

/// <summary>An output message from the compiler.</summary>
public class OutputMessage
{
  /// <summary>Creates an output message with the given type and text.</summary>
  public OutputMessage(OutputMessageType type, string message)
  {
    this.Message = message;
    this.Type    = type;
  }

  /// <summary>Creates an output message with the given type and text, and associated with a position in a source file.</summary>
  public OutputMessage(OutputMessageType type, string message, string sourceName, Position position)
    : this(type, message)
  {
    this.SourceName = sourceName;
    this.Position   = position;
  }

  /// <summary>Creates an output message with the given type and text, associated with a position in a source file, and
  /// containing an instance of the exception that caused this message.
  /// </summary>
  public OutputMessage(OutputMessageType type, string message, string sourceName, Position position,
                       Exception exception)
    : this(type, message, sourceName, position)
  {
    this.Exception = exception;
  }

  /// <summary>Formats this message for display.</summary>
  public override string ToString()
  {
    return string.Format("{0}({1},{2}): {3}", SourceName, Position.Line, Position.Column, Message);
  }

  /// <summary>The source file name related to the error, if available.</summary>
  public string SourceName;
  /// <summary>The position within the source file related to the error, if available.</summary>
  public Position Position;

  /// <summary>The message to display to the user.</summary>
  public string Message;
  /// <summary>The exception that caused this error, if any.</summary>
  public Exception Exception;
  
  /// <summary>The type of output message.</summary>
  public OutputMessageType Type;
}

/// <summary>A collection of compiler output messages.</summary>
public sealed class OutputMessageCollection : Collection<OutputMessage>
{
  /// <summary>Gets whether or not any error messages have been added to the message collection.</summary>
  public bool HasErrors
  {
    get
    {
      foreach(OutputMessage message in this)
      {
        if(message.Type == OutputMessageType.Error)
        {
          return true;
        }
      }

      return false;
    }
  }

  /// <summary>Inserts an item while ensuring that it is not null.</summary>
  protected override void InsertItem(int index, OutputMessage item)
  {
    if(item == null) throw new ArgumentNullException();
    base.InsertItem(index, item);
  }

  /// <summary>Sets an item while ensuring that it is not null.</summary>
  protected override void SetItem(int index, OutputMessage item)
  {
    if(item == null) throw new ArgumentNullException();
    base.SetItem(index, item);
  }
}
#endregion

} // namespace Scripting.AST