namespace VisitorManagementAI.Models;

public record JsonRpcRequest(string Method, object Params, object Id, string Jsonrpc = "2.0");

public record JsonRpcResponse<T>(T Result, JsonRpcError Error);

public record JsonRpcError(int Code, string Message);