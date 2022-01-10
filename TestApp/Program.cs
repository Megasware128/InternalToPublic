using Megasware128.InternalToPublic;

[assembly: InternalToPublic("System.Linq.Expressions", "System.Linq.Expressions.Error")]

var error = Error.ReducibleMustOverrideReduce();

Console.WriteLine(error.Message);
