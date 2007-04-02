using System;
using System.Collections.ObjectModel;

namespace Scripting.AST
{

public abstract class CompilerBase
{
  internal CompilerBase() { }

  public bool HasErrors
  {
    get { return Messages.HasErrors; }
  }

  public readonly OutputMessageCollection Messages = new OutputMessageCollection();
}

public class Compiler<OptionType> : CompilerBase where OptionType : CompilerOptions, new()
{
  public OptionType Options
  {
    get { return options; }
  }

  protected OptionType options = new OptionType();
}

public class CompilerOptions
{
  public bool Optimized, Debug;

  protected void CloneTo(CompilerOptions options)
  {
    options.Optimized = Optimized;
    options.Debug = Debug;
  }
}

public abstract class CompilerUserBase<CompilerType> where CompilerType : CompilerBase
{
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

  /// <summary>Adds an output message to <see cref="CompilerState"/>.</summary>
  protected void AddMessage(OutputMessage message)
  {
    compiler.Messages.Add(message);
  }

  /// <summary>Adds a new error message using the given source name and position.</summary>
  protected void AddErrorMessage(string sourceName, FilePosition position, string message)
  {
    AddMessage(new OutputMessage(OutputMessageType.Error, message, sourceName, position));
  }

  readonly CompilerType compiler;
}

#region OutputMessage, OutputMessageType, and OutputMessageCollection
/// <summary>The type of an output message from the compiler.</summary>
public enum OutputMessageType
{
  Information, Warning, Error
}

/// <summary>An output message from the compiler.</summary>
public class OutputMessage
{
  public OutputMessage(OutputMessageType type, string message)
  {
    this.Message = message;
    this.Type    = type;
  }
  
  public OutputMessage(OutputMessageType type, string message, string sourceName, FilePosition position)
    : this(type, message)
  {
    this.SourceName = sourceName;
    this.Position   = position;
  }

  public OutputMessage(OutputMessageType type, string message, string sourceName, FilePosition position,
                       Exception exception)
    : this(type, message, sourceName, position)
  {
    this.Exception = exception;
  }

  public override string ToString()
  {
    return string.Format("{0}({1},{2}): {3}", SourceName, Position.Line, Position.Column, Message);
  }

  /// <summary>The source file name related to the error, if available.</summary>
  public string SourceName;
  /// <summary>The position within the source file related to the error, if available.</summary>
  public FilePosition Position;

  /// <summary>The message to display to the user.</summary>
  public string Message;
  /// <summary>The exception that caused this error, if any.</summary>
  public Exception Exception;
  
  /// <summary>The type of output message.</summary>
  public OutputMessageType Type;
}

/// <summary>A collection of compiler output messages.</summary>
public class OutputMessageCollection : Collection<OutputMessage>
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

  protected override void InsertItem(int index, OutputMessage item)
  {
    if(item == null) throw new ArgumentNullException(); // disallow null messages
    base.InsertItem(index, item);
  }

  protected override void SetItem(int index, OutputMessage item)
  {
    if(item == null) throw new ArgumentNullException(); // disallow null messages
    base.SetItem(index, item);
  }
}
#endregion

} // namespace Scripting.AST