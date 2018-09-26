/*
MIT License

Copyright(c) 2018 Patti4832

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TCPAutoResponder
{
    class TcpAutoResponder
    {
        /// <summary>
        /// Type of a TcpAutoResponder object
        /// </summary>
        public enum Type
        {
            Server,
            Client
        }

        private Type type;
        private string hostname;
        private int port;
        private List<Response> responses = new List<Response>();
        private bool running = true;
        private string loginMessage;
        private int delayLoginMessage;
        private readonly bool ignoreReadErrors = true;
        private readonly bool ignoreWriteErrors = true;
        private Thread thread;

        /// <summary>
        /// Creates a TcpAutoResponder object to answer requests automatically
        /// </summary>
        /// <param name="type">Server or Client mode</param>
        /// <param name="hostname">Hostname; leave blank if you run a server</param>
        /// <param name="port">Port</param>
        /// <param name="responses">List of responses you want to get answered</param>
        /// <param name="loginMessage">Message which is sent after the client is connected</param>
        /// <param name="delayLoginMessage">Delay between connection is established and sending the login message</param>
        public TcpAutoResponder(Type type, string hostname, int port, List<Response> responses, string loginMessage = "", int delayLoginMessage = 5)
        {
            this.type = type;
            
            this.hostname = hostname;

            if (port < 65536 && port > 0)
                this.port = port;
            else
                throw new TcpAutoResponderException("Port outside the allowed range");

            this.loginMessage = loginMessage;

            if (delayLoginMessage >= 0)
                this.delayLoginMessage = delayLoginMessage;
            else
                throw new TcpAutoResponderException("Delay minimum is zero");

            foreach (Response r in responses)
            {
                this.responses.Add(r);
            }

            if (type == Type.Server)
            {
                thread = new Thread(() =>
                {
                    RunServer();
                });

                try
                {
                    thread.Start();
                }
                catch (Exception e)
                {
                    throw new TcpAutoResponderException("Can't start thread", e);
                }
            }
            else if (type == Type.Client)
            {
                thread = new Thread(() =>
                {
                    TcpClient client;
                    try
                    {
                        client = new TcpClient(hostname, port);
                    }
                    catch (Exception e)
                    {
                        throw new TcpAutoResponderException("Can't connect to server", e);
                    }

                    RunClient(client);
                });

                try
                {
                    thread.Start();
                }
                catch (Exception e)
                {
                    throw new TcpAutoResponderException("Can't start thread", e);
                }
            }
            else
            {
                throw new TcpAutoResponderException("Unknown type");
            }
        }

        /// <summary>
        /// Adds a new response to this TcpAutoResponder object
        /// </summary>
        /// <param name="responder">Responder object</param>
        public void RegisterResponder(Response response)
        {
            responses.Add(response);
        }

        /// <summary>
        /// Stops the loop
        /// </summary>
        public void Stop()
        {
            running = false;
        }

        private void RunServer()
        {
            TcpListener listener;
            try
            {
                listener = new TcpListener(port);
            }
            catch (Exception e)
            {
                throw new TcpAutoResponderException("Can't create server", e);
            }

            try
            {
                listener.Start();
            }
            catch (Exception e)
            {
                throw new TcpAutoResponderException("Can't start server", e);
            }

            while (running)
            {
                TcpClient client;

                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception e)
                {
                    throw new TcpAutoResponderException("Can't accept client", e);
                }

                Thread thread1 = new Thread(() =>
                {
                    RunClient(client);
                });

                try
                {
                    thread1.Start();
                }
                catch (Exception e)
                {
                    throw new TcpAutoResponderException("Can't start thread", e);
                }
            }
        }
        private void RunClient(TcpClient client)
        {
            Stream stream;

            try
            {
                stream = client.GetStream();
            }
            catch(Exception e)
            {
                throw new TcpAutoResponderException("Can't get client stream", e);
            }

            try
            {
                Thread.Sleep(delayLoginMessage);
            }
            catch(Exception e)
            {
                throw new TcpAutoResponderException("Can't delay", e);
            }

            if (!loginMessage.Equals(""))
            {
                WriteStream(stream, loginMessage);
            }

            while (running)
            {
                string s = ReadStream(stream);

                //Debug only
                //Console.WriteLine(s);

                if (!running)
                    break;


                foreach (Response response in responses)
                {
                    if (response.CheckResponse(s))
                    {
                        WriteStream(stream, response.GetResponse());
                    }
                }
            }
        }

        private string ReadStream(Stream stream)
        {
            string tmp = "";

            int requestLength = 0;
            byte[] requestBytes = new byte[4096];
            bool receivedError = false;

            try
            {
                requestLength = stream.Read(requestBytes, 0, 4096);
            }
            catch (Exception e)
            {
                receivedError = true;

                if (!ignoreReadErrors)
                {
                    throw new TcpAutoResponderException("Error while reading the stream", e);
                }
            }

            if (requestLength != 0 && !receivedError)
            {
                ASCIIEncoding encoder = new ASCIIEncoding();
                try
                {
                    tmp = encoder.GetString(requestBytes, 0, requestLength);
                }
                catch(Exception e)
                {
                    throw new TcpAutoResponderException("Encoding failed", e);
                }
            }

            return tmp;
        }
        private bool WriteStream(Stream stream, string content)
        {
            bool tmp = true;

            int responseLength = content.Length;
            byte[] responseBytes;

            try
            {
                responseBytes = Encoding.ASCII.GetBytes(content.ToCharArray());
            }
            catch (Exception e)
            {
                throw new TcpAutoResponderException("Can't get bytes from response-string", e);
            }

            try
            {
                stream.Write(responseBytes, 0, responseLength);
            }
            catch (Exception e)
            {
                tmp = false;

                if (!ignoreWriteErrors)
                {
                    throw new TcpAutoResponderException("Error while writing to stream", e);
                }
            }

            return tmp;
        }

        /// <summary>
        /// Response for a TcpAutoResponder
        /// </summary>
        public class Response
        {
            /// <summary>
            /// Choose in which case the request is the right for your answer
            /// </summary>
            public enum RequestDetectionMode
            {
                Contains,
                Equals,
                StartsWith,
                EndsWith
            }

            private string request;
            private string response;
            private string filename;
            private bool ignoreCase;
            private RequestDetectionMode mode;
            private Encoding encoding;

            /// <summary>
            /// Creates a response object with request and response
            /// </summary>
            /// <param name="request">Incomming string</param>
            /// <param name="response">Outgoing string</param>
            /// <param name="ignoreCase">Ignore Case</param>
            /// <param name="mode">Case of triggering a response</param>
            public Response(string request, string response, bool ignoreCase, RequestDetectionMode mode)
            {
                this.request = request;
                this.response = response;
                this.ignoreCase = ignoreCase;
                this.mode = mode;
            }

            /// <summary>
            /// Creates a response object which can send a file
            /// </summary>
            /// <param name="request">Incomming string</param>
            /// <param name="response">Outgoing string; include the file content with "[content]"</param>
            /// <param name="filename">Path and filename of a file which you want to send</param>
            /// <param name="encoding">Encoding for the file</param>
            /// <param name="ignoreCase">Ignore Case</param>
            public Response(string request, string response, string filename, bool ignoreCase, RequestDetectionMode mode, Encoding encoding)
            {
                this.request = request;
                this.response = response;
                this.ignoreCase = ignoreCase;
                this.filename = filename;
                this.encoding = encoding;
                this.mode = mode;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <returns>request string</returns>
            public string GetRequest()
            {
                return request;
            }

            /// <summary>
            /// 
            /// </summary>
            /// <returns>response string; "[content]" is replaced with the content of the file, if set</returns>
            public string GetResponse()
            {
                string tmp = "";

                if (filename == null)
                {
                    tmp = response;
                }
                else
                {
                    string fromFile;

                    try
                    {
                        fromFile = File.ReadAllText(filename, encoding);
                    }
                    catch(Exception e)
                    {
                        throw new TcpAutoResponderException("Can't read from file", e);
                    }

                    try
                    {
                        tmp = response.Replace("[content]", fromFile);
                    }
                    catch(Exception e)
                    {
                        throw new TcpAutoResponderException("Can't insert file content in the response", e);
                    }
                }

                return tmp;
            }

            /// <summary>
            /// Checks if the request matches the set request of this response
            /// </summary>
            /// <param name="request">Request from the server/client</param>
            /// <returns>True if it matches the set request of this object</returns>
            public bool CheckResponse(string request)
            {
                bool tmp = false;

                if (ignoreCase)
                {
                    this.request = this.request.ToLower();
                    request = request.ToLower();
                }

                if (mode == RequestDetectionMode.Equals)
                {
                    if (request.Equals(this.request))
                        tmp = true;
                }
                else if (mode == RequestDetectionMode.Contains)
                {
                    if (request.Contains(this.request))
                        tmp = true;
                }
                else if (mode == RequestDetectionMode.StartsWith)
                {
                    if (request.StartsWith(this.request))
                        tmp = true;
                }
                else if (mode == RequestDetectionMode.EndsWith)
                {
                    if (request.EndsWith(this.request))
                        tmp = true;
                }

                return tmp;
            }
        }
    }

    [Serializable()]
    class TcpAutoResponderException : Exception
    {
        public TcpAutoResponderException() { }
        public TcpAutoResponderException(string message) : base(message) { }
        public TcpAutoResponderException(string message, Exception inner) : base(message, inner) { }

        protected TcpAutoResponderException(System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context)
        { }
    }
}