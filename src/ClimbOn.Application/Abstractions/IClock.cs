namespace ClimbOn.Application.Abstractions;

// C4: the only source of the current time outside Domain, which takes `now` as a parameter.
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
