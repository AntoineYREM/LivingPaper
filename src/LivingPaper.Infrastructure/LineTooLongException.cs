namespace LivingPaper.Infrastructure;

// A line of a stream went over the limit of BoundedLineReader. It holds none of the line.
internal sealed class LineTooLongException() : Exception("A line of the stream is too long.");
