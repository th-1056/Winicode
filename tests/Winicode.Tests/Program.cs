using Winicode;

var failures=new List<string>();
var catalog=UnicodeCatalog.Load(Path.Combine(AppContext.BaseDirectory,"data"));
failures.AddRange(CatalogTests.Run(catalog));
failures.AddRange(KeyboardTests.Run());
failures.AddRange(ServiceTests.Run());
failures.AddRange(RuntimeTests.Run());
foreach(var failure in failures)Console.Error.WriteLine(failure);
Console.WriteLine($"Winicode tests: {failures.Count} failure(s); Unicode {catalog.Version}, {catalog.NamedCount} named entries.");
return failures.Count==0?0:1;
