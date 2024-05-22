

// private static string TrimFunctionSchemaBody(string body)
// {
//     var trimmedBody = body.Trim();

//     if (trimmedBody.Length < 2)
//     {
//         throw new InvalidOperationException(
//             $"Function body should at least be 2 characters but isn't:  {body}");
//     }
//     if (trimmedBody.First() != '{' || trimmedBody.Last() != '}')
//     {
//         throw new InvalidOperationException(
//             $"Function body was expected to be surrounded by curly brace but isn't:"
//             + $"  {body}");
//     }

//     var actualBody = trimmedBody
//         .Substring(1, trimmedBody.Length - 2)
//         //  This trim removes the carriage return so they don't accumulate in translations
//         .Trim();

//     return actualBody;
// }



// Console.WriteLine("Hello World!");



var code = KustoCode.Parse(script);