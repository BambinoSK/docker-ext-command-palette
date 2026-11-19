using System;
using System.Net;

namespace DockerExtension.Docker;

internal sealed class DockerApiException : Exception
{
    public DockerApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public DockerApiException()
    {
    }

    public DockerApiException(string message)
        : base(message)
    {
    }

    public DockerApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public HttpStatusCode StatusCode { get; }
}

internal sealed class DockerUnavailableException : Exception
{
    public DockerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DockerUnavailableException()
    {
    }

    public DockerUnavailableException(string message)
        : base(message)
    {
    }
}
