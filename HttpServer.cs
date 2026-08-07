using System.Net;
using System.Threading.Tasks;

namespace Terragit;

public class HttpServer
{
    private HttpListener listener;
    
    public async void Start()
    {
        // Inits the HttpListener
        listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8080/");
        listener.Start();

        Task.Run( async () =>
        {

            while (listener.IsListening)
            {
                try
                {
                    // Waits for a request
                    HttpListenerContext context = await listener.GetContextAsync();

                    // Gets the request and response object to send back
                    HttpListenerRequest request = context.Request;
                    HttpListenerResponse response = context.Response;

                    await HandleResponse(request, response);

                }
                catch (HttpListenerException)
                {
                    // Prevents printing an error when shutting down the server
                    break;
                }
                
            }
        }
        );
    }

    public async Task HandleResponse(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.Url == null)
            return;
        
        string requestedPath = request.Url.AbsolutePath;
        string responseText;
        if (requestedPath == "/git/init")
        {
            responseText = "<html><body> Hey </body></html>";
        }
        else
        {
            responseText = "<html><body>no</body></html>";
        }

        // convert text to bytes
        byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseText);
        response.ContentLength64 =  buffer.Length;
        
        // Write text to the response stream and close it
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    public void Stop()
    {
        listener.Stop();
        listener.Close();
    }
}

