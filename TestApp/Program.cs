using Megasware128.InternalToPublic;

[assembly: InternalToPublic("System.Linq.Expressions", "System.Linq.Expressions.Error")]
[assembly: InternalToPublic("Newtonsoft.Json", "Newtonsoft.Json.Utilities.DateTimeUtils")]

var error = Error.ReducibleMustOverrideReduce();

Console.WriteLine(error);

Console.WriteLine(DateTimeUtils.ToUniversalTicks(DateTime.Now));
