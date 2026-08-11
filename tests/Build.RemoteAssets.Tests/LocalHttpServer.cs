using System.Net;

namespace Kebechet.Build.RemoteAssets.Tests;

/// <summary>
/// Serves canned payloads over loopback so the build tests never touch the network, and counts
/// requests per path so "the second build did not download again" is an assertion rather than an
/// inference from timing.
/// </summary>
public sealed class LocalHttpServer : IDisposable
{
	private readonly HttpListener _listener;
	private readonly Dictionary<string, byte[]> _payloadsByPath;
	private readonly Dictionary<string, int> _requestCountsByPath = new();
	private readonly Lock _countsLock = new();

	public string BaseUrl { get; }

	public LocalHttpServer(Dictionary<string, byte[]> payloadsByPath)
	{
		_payloadsByPath = payloadsByPath;

		var port = GetFreePort();
		BaseUrl = $"http://localhost:{port}";

		_listener = new HttpListener();
		_listener.Prefixes.Add($"{BaseUrl}/");
		_listener.Start();

		_ = Task.Run(Listen);
	}

	public int RequestCountFor(string path)
	{
		lock (_countsLock)
		{
			return _requestCountsByPath.TryGetValue(path, out var count)
				? count
				: 0;
		}
	}

	private async Task Listen()
	{
		while (_listener.IsListening)
		{
			HttpListenerContext context;
			try
			{
				context = await _listener.GetContextAsync();
			}
			catch (Exception)
			{
				return;
			}

			var path = context.Request.Url!.AbsolutePath.TrimStart('/');

			lock (_countsLock)
			{
				_requestCountsByPath[path] = RequestCountFor(path) + 1;
			}

			if (_payloadsByPath.TryGetValue(path, out var payload))
			{
				context.Response.StatusCode = (int) HttpStatusCode.OK;
				context.Response.ContentLength64 = payload.Length;
				await context.Response.OutputStream.WriteAsync(payload);
			}
			else
			{
				context.Response.StatusCode = (int) HttpStatusCode.NotFound;
			}

			context.Response.Close();
		}
	}

	private static int GetFreePort()
	{
		var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint) listener.LocalEndpoint).Port;
		listener.Stop();

		return port;
	}

	public void Dispose()
	{
		if (_listener.IsListening)
		{
			_listener.Stop();
		}

		_listener.Close();
	}
}
