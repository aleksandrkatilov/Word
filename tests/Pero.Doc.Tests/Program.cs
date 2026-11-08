using System.Text;
using Pero.Doc.Tests;

Console.OutputEncoding = Encoding.UTF8;
RoundTripTests.Register(args.FirstOrDefault());
EditTests.Register(args.FirstOrDefault());
return T.Run();
