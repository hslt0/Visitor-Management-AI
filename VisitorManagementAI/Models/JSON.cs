namespace VisitorManagementAI.Models;

/// <summary>
/// Represents a JSON-RPC 2.0 request object.
/// </summary>
/// <param name="Method">The name of the method to be invoked.</param>
/// <param name="Params">A structured value that holds the parameter values to be used during the invocation of the method.</param>
/// <param name="Id">An identifier established by the Client that MUST contain a String, Number, or NULL value if included.</param>
/// <param name="Jsonrpc">A String specifying the version of the JSON-RPC protocol. MUST be exactly "2.0".</param>
public record JsonRpcRequest(string Method, object Params, object Id, string Jsonrpc = "2.0");

/// <summary>
/// Represents a JSON-RPC 2.0 response object.
/// </summary>
/// <typeparam name="T">The type of the result object.</typeparam>
/// <param name="Result">The value of this member is determined by the method invoked on the Server.</param>
/// <param name="Error">The error object if an error occurred.</param>
public record JsonRpcResponse<T>(T Result, JsonRpcError Error);

/// <summary>
/// Represents a JSON-RPC 2.0 error object.
/// </summary>
/// <param name="Code">A Number that indicates the error type that occurred.</param>
/// <param name="Message">A String providing a short description of the error.</param>
public record JsonRpcError(int Code, string Message);