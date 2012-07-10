using System;
using System.Collections.Generic;
using System.IO;
using Scripting;

namespace Scripting.AST
{

#region Position
/// <summary>This struct represents a position within a text document.</summary>
public struct Position
{
  /// <summary>Initializes the position with the given line and column.</summary>
  public Position(int line, int column)
  {
    Line   = line;
    Column = column;
  }

  /// <summary>Converts the position to a human-readable string.</summary>
  public override string ToString()
  {
    return Line.ToString() + "," + Column.ToString();
  }

  /// <summary>The one-based line or column index.</summary>
  public int Line, Column;
}
#endregion

#region Span
/// <summary>This struct represents a span within a text document.</summary>
public struct Span
{
  /// <summary>Initializes the span from two positions.</summary>
  public Span(Position start, Position end)
  {
    Start = start;
    End   = end;
  }

  /// <summary>Converts the span to a human-readable string.</summary>
  public override string ToString()
  {
    return Start.ToString() + " - " + End.ToString();
  }

  /// <summary>The start or end position in the span.</summary>
  public Position Start, End;
}
#endregion

#region FilePosition
/// <summary>This struct represents a position within a source file.</summary>
public struct FilePosition
{
  /// <summary>Initializes the file position from a source name and a position within the source.</summary>
  public FilePosition(string sourceName, Position position)
  {
    Position   = position;
    SourceName = sourceName;
  }

  /// <summary>Converts the file position to a human-readable string.</summary>
  public override string ToString()
  {
    return SourceName+"(" + Position.ToString() + ")";
  }

  /// <summary>The name of the source file.</summary>
  public string SourceName;
  /// <summary>The position within the source file.</summary>
  public Position Position;
}
#endregion

#region FileSpan
/// <summary>This struct represents a span within a source file.</summary>
public struct FileSpan
{
  /// <summary>Initializes this file span from a source name and the start and end positions within it.</summary>
  public FileSpan(string sourceName, Position start, Position end)
  {
    Span       = new Span(start, end);
    SourceName = sourceName;
  }

  /// <summary>Initializes this file span with a file name and a span.</summary>
  public FileSpan(string sourceName, Span span)
  {
    Span       = span;
    SourceName = sourceName;
  }

  /// <summary>Converts the file span to a human-readable string.</summary>
  public override string ToString()
  {
    return SourceName+"("+Span.ToString()+")";
  }

  /// <summary>The source file name.</summary>
  public string SourceName;
  /// <summary>The span within the source file.</summary>
  public Span Span;
}
#endregion

#region Token
/// <summary>Represents a single language token.</summary>
public struct Token<TokenType,ValueType>
{
  /// <summary>The type of the token. This value can be arbitrary, and is only expected to be understood by the parser.</summary>
  public TokenType Type;
  /// <summary>The name of the source file from which the token was read. This does not need to be a file on disk. It
  /// can be an in-memory file, a URL, etc. Diagnostic tools such as debuggers will attempt to read the data from this
  /// source file.
  /// </summary>
  public string SourceName;
  /// <summary>The start position of the token's span within the file.</summary>
  public Position Start;
  /// <summary>The end position of the token's span within the file. The end position is inclusive, pointing to the
  /// last character of the token.
  /// </summary>
  public Position End;
  /// <summary>An arbitrary value associated with this token. For instance, numeric tokens might pass the numeric value
  /// in this field.
  /// </summary>
  public ValueType Data;

  /// <summary>Returns the <see cref="Type"/> field, converted to a string.</summary>
  public override string ToString()
  {
    return Convert.ToString(Type);
  }
}
#endregion

#region IScanner
/// <summary>An interface that represents a scanner (also called a lexer or tokenizer).</summary>
public interface IScanner<TokenType>
{
  /// <summary>Retrieves the next token in the stream.</summary>
  /// <returns>True if a token was retrieved and false otherwise.</returns>
  /// <remarks>If all tokens have been exhausted, this method should populate <paramref name="token"/> with a token
  /// type recognizable as an EOF in addition to returning false, if possible.
  /// </remarks>
  bool NextToken(out TokenType token);
  /// <summary>Pushes a token back onto the token stream. Scanners should support an unlimited number of pushed-back
  /// tokens.
  /// </summary>
  void PushBack(TokenType token);
}
#endregion

#region ScannerBase
/// <summary>Provides a helper class for implementing scanners.</summary>
/// <remarks>You are not required to use this class when you implement scanners. This class exists only to provide a
/// part of the <see cref="IScanner{T}"/> implementation.
/// </remarks>
public abstract class ScannerBase<CompilerType,TokenType>
  : CompilerUserBase<CompilerType>, IScanner<TokenType> where CompilerType : CompilerBase
{
  /// <summary>
  /// Initializes the scanner with a list of source names. The source files will be loaded based on these names.
  /// </summary>
  protected ScannerBase(CompilerType compiler, params string[] sourceNames) : base(compiler)
  {
    if(sourceNames == null) throw new ArgumentNullException();
    this.sourceNames = sourceNames;
    ValidateSources();
  }

  /// <summary>Initializes the scanner with a list of streams. The source names of the streams will be
  /// "&lt;unknown&gt;".
  /// </summary>
  protected ScannerBase(CompilerType compiler, params TextReader[] sources) : base(compiler)
  {
    if(sources == null) throw new ArgumentNullException();
    this.sources = sources;
    ValidateSources();
  }

  /// <summary>Initializes the scanner with a list of streams and their names.</summary>
  protected ScannerBase(CompilerType compiler, TextReader[] sources, string[] sourceNames) : base(compiler)
  {
    if(sources == null || sourceNames == null) throw new ArgumentNullException();
    if(sources.Length != sourceNames.Length)
    {
      throw new ArgumentException("Number of source names doesn't match number of sources.");
    }
    this.sources     = sources;
    this.sourceNames = sourceNames;
    ValidateSources();
  }

  /// <summary>Gets the one-based column index within the current source line.</summary>
  public int Column
  {
    get { return sourceState.Position.Column; }
  }

  /// <summary>Gets the one-based line index within the current source file.</summary>
  public int Line
  {
    get { return sourceState.Position.Line; }
  }

  /// <summary>Gets the current position within the source file.</summary>
  public Position Position
  {
    get { return sourceState.Position; }
  }

  /// <summary>Gets the name of the current source. You must call <see cref="NextSource"/> at least once before this
  /// will be valid.
  /// </summary>
  public string SourceName
  {
    get
    {
      AssertValidSource();
      return sourceNames == null ? "<unknown>" : sourceNames[sourceIndex];
    }
  }

  /// <summary>Retrieves the next token.</summary>
  /// <returns>True if the next token was retrieved and false otherwise.</returns>
  public bool NextToken(out TokenType token)
  {
    if(pushedTokens != null && pushedTokens.Count != 0)
    {
      token = pushedTokens.Dequeue();
      return true;
    }
    else
    {
      return ReadToken(out token);
    }
  }

  /// <summary>Pushes a token back onto the token stream.</summary>
  public void PushBack(TokenType token)
  {
    if(pushedTokens == null) pushedTokens = new Queue<TokenType>();
    pushedTokens.Enqueue(token);
  }

  /// <summary>Gets the current character. This is not valid until <see cref="NextSource"/> has been called at least
  /// once.
  /// </summary>
  protected char Char
  {
    get { return sourceState.Char; }
  }

  /// <summary>Gets the position of the previous character within the source file.</summary>
  protected Position LastPosition
  {
    get { return sourceState.LastPosition; }
  }

  /// <summary>Gets whether a source is loaded and whether <see cref="SourceName"/>, etc are valid.</summary>
  protected bool HasValidSource
  {
    get { return textData != null; }
  }

  /// <summary>Adds a new error message using the current source name and position.</summary>
  protected void AddErrorMessage(string message)
  {
    AddErrorMessage(SourceName, Position, message);
  }

  /// <summary>Adds a new error message using the current source name and the given position.</summary>
  protected void AddErrorMessage(Position position, string message)
  {
    AddErrorMessage(SourceName, position, message);
  }

  /// <summary>Ensures that a valid source file is loaded.</summary>
  /// <remarks>
  /// If <see cref="HasValidSource"/> is false, this method calls <see cref="NextSource"/> to move to the next source.
  /// </remarks>
  /// <returns>True if a valid source file is loaded, and false if there are no more source files.</returns>
  protected bool EnsureValidSource()
  {
    return HasValidSource || NextSource();
  }

  /// <summary>Loads a data stream, given its source name.</summary>
  protected virtual TextReader LoadSource(string name)
  {
    return new StreamReader(name);
  }

  /// <summary>Reads the next character from the input stream.</summary>
  /// <returns>Returns the next character, or the nul (0) character if there is no more input in the current source.</returns>
  protected char NextChar()
  {
    AssertValidSource();

    // save the position of the current character. we do this even in the case of EOF
    sourceState.LastPosition = sourceState.Position;

    // if we were at the end of a line before, update the position to the next line. this is done here so that newline
    // characters are positioned at the end of the line they're on rather than the beginning of the next line
    if(sourceState.AtEOL)
    {
      sourceState.Position.Line++;
      sourceState.Position.Column = 0;
      sourceState.AtEOL = false;
    }

    if(sourceState.DataIndex >= textData.Length) // or, if we've reached the end of input, return the nul character
    {
      if(sourceState.Char != 0) sourceState.Position.Column++;
      sourceState.Char = '\0';
      return sourceState.Char;
    }
    else // otherwise, read the next input charactr
    {
      sourceState.Char = textData[sourceState.DataIndex++];
      sourceState.Position.Column++;
    }

    if(sourceState.Char == '\n') // if it's a newline, move the pointer to the next line
    {
      sourceState.AtEOL = true;
    }
    else if(sourceState.Char == '\r')
    {
      // if it's a carriage return from a CRLF pair, skip over the carriage return.
      if(sourceState.DataIndex < textData.Length && textData[sourceState.DataIndex] == '\n')
      {
        sourceState.DataIndex++;
      }
      // in any case, treat the carriage return like a newline
      sourceState.Char  = '\n';
      sourceState.AtEOL = true;
    }
    // if it's an embedded nul character, convert it to a space (we're using nul characters to signal EOF)
    else if(sourceState.Char == '\0')
    {
      sourceState.Char = ' ';
    }

    return sourceState.Char;
  }

  /// <summary>Advances to the next input stream and calls <see cref="NextChar"/> on it.</summary>
  /// <returns>Returns true if the current data has been set to the next input source and false if all input sources
  /// have been consumed.
  /// </returns>
  protected bool NextSource()
  {
    int maxSources = sources == null ? sourceNames.Length : sources.Length;
    if(sourceIndex == maxSources) return false; // if we've already consumed all the sources, return false

    sourceIndex++;
    if(sourceIndex >= maxSources) // if there no more sources, return false
    {
      textData = null;
      return false;
    }
    else // otherwise, there are still sources...
    {
      if(sources == null) // if they weren't provided in the constructor, load the next source by name
      {
        using(TextReader reader = LoadSource(sourceNames[sourceIndex]))
        {
          textData = reader.ReadToEnd();
        }
      }
      else // otherwise use what the user provided
      {
        textData = sources[sourceIndex].ReadToEnd();
      }
      
      sourceState.DataIndex = 0;
      sourceState.LastPosition = sourceState.Position = new Position(1, 0); // the NextChar() will advance to the first column
      savedState = sourceState;
      OnSourceLoaded();
      NextChar();
      return true;
    }
  }

  /// <summary>Called after a source is loaded.</summary>
  /// <remarks>Derived classes might override this method to clear per-source state.</remarks>
  protected virtual void OnSourceLoaded() { }

  /// <summary>Reads the next token from the input.</summary>
  /// <returns>Returns true if the next token was read and false if there are no more tokens in any input stream.</returns>
  protected abstract bool ReadToken(out TokenType token);

  /// <summary>Saves the state of the current source. This allows lookahead.</summary>
  /// <remarks>Characters can be read with <see cref="NextChar"/> and then <see cref="RestoreState"/> can be called to
  /// restore the position within the source to the point where this method was called. There is no stack of sources,
  /// so it's not required to call <see cref="RestoreState"/>, but you cannot push multiple states either.
  /// Note that the state cannot be saved and restored across different data sources.
  /// </remarks>
  protected void SaveState()
  {
    savedState = sourceState;
  }

  /// <summary>Restores the state of the current source to the way it was when <see cref="SaveState"/> was last called.</summary>
  /// <remarks>There is no stack of sources so it's not required to call <see cref="RestoreState"/>.</remarks>
  protected void RestoreState()
  {
    sourceState = savedState;
  }

  /// <summary>Skips over whitespace, including newlines.</summary>
  /// <returns>Returns the next non-whitespace character.</returns>
  protected char SkipWhitespace()
  {
    return SkipWhitespace(true);
  }

  /// <summary>Skips over whitespace.</summary>
  /// <param name="skipNewLines">If true, newline characters will be skipped over.</param>
  /// <returns>Returns the next non-whitespace character.</returns>
  protected char SkipWhitespace(bool skipNewLines)
  {
    while((skipNewLines || Char != '\n') && char.IsWhiteSpace(Char))
    {
      NextChar();
    }
    return Char;
  }

  struct State
  {
    public Position Position, LastPosition;
    public int DataIndex;
    public char Char;
    public bool AtEOL;
  }

  /// <summary>Asserts that <see cref="NextSource"/> has been called and has moved to a valid source.</summary>
  void AssertValidSource()
  {
    if(textData == null)
    {
      throw new InvalidOperationException("The scanner is not positioned at a valid source.");
    }
  }
  
  /// <summary>Validates that none of the array items passed to the constructor are null.</summary>
  void ValidateSources()
  {
    if(sources != null)
    {
      foreach(TextReader reader in sources)
      {
        if(reader == null) throw new ArgumentException("A text reader was null.");
      }
    }

    if(sourceNames != null)
    {
      foreach(string name in sourceNames)
      {
        if(name == null) throw new ArgumentException("A source name was null.");
      }
    }
  }

  Queue<TokenType> pushedTokens;
  readonly string[] sourceNames;
  readonly TextReader[] sources;
  State sourceState, savedState;
  string textData;
  int sourceIndex = -1;
}
#endregion

} // namespace Scripting.AST