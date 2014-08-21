using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace restlessmedia.UI.Api.Extensions
{
  public static class RequestMessageExtensions
  {
    public static Task<HttpResponseMessage> UploadAsync(this HttpRequestMessage request, FileInfo file, Action<bool> done)
    {
      return UploadAsync(request, (parent, headers) => !file.Exists ? file.Create() : file.Open(parent.Headers.ContentRange != null ? FileMode.Append : FileMode.Create, FileAccess.Write), done);
    }

    public static Task<HttpResponseMessage> UploadAsync(this HttpRequestMessage request, string path, Action<bool> done)
    {
      if (string.IsNullOrEmpty(path))
        throw new ArgumentNullException("path");

      return UploadAsync(request, new FileInfo(path), done);
    }

    public static Task<HttpResponseMessage> UploadAsync(this HttpRequestMessage request, Func<HttpContentHeaders, Stream> streamHandler, Action<bool> done)
    {
      return UploadAsync(request, (parent, headers) => streamHandler(headers), done);
    }

    public static Task<HttpResponseMessage> UploadAsync(this HttpRequestMessage request, Func<HttpContent, HttpContentHeaders, Stream> streamHandler, Action<bool> done)
    {
      return UploadAsync<HttpResponseMessage>(request, streamHandler, (fileData, complete) =>
      {
        done(complete);
        return OkResponse(request);
      }, x => FailedResponse(request));
    }

    public static Task<HttpResponseMessage> UploadAsync(this HttpRequestMessage request, IDirectoryInfo directory, Action<NameValueCollection, Collection<MultipartFileData>, bool> done)
    {
      return UploadAsync(request, directory, (formData, fileData, complete) =>
      {
        done(formData, fileData, complete);
        return OkResponse(request);
      }, x => FailedResponse(request));
    }

    public static Task<HttpResponseMessage> UploadAsync(this HttpRequestMessage request, DirectoryInfo directory, Action<NameValueCollection, Collection<MultipartFileData>, bool> done)
    {
      return UploadAsync(request, new DirectoryInfoWrapper(directory), done);
    }

    public static Task<T> UploadAsync<T>(this HttpRequestMessage request, Func<HttpContent, HttpContentHeaders, Stream> streamHandler, Func<Collection<MultipartFileData>, bool, T> done, Func<Exception, T> fail)
    {
      return UploadAsync(request, new StreamProviderWrapper(streamHandler), (provider, complete) => done(provider.FileData, complete), fail);
    }

    public static Task<T> UploadAsync<T>(this HttpRequestMessage request, IDirectoryInfo directory, Func<NameValueCollection, Collection<MultipartFileData>, bool, T> done, Func<Exception, T> fail)
    {
      return UploadAsync(request, new MultipartFormDataStreamProvider(directory.FullName), (provider, complete) => done(provider.FormData, provider.FileData, complete), fail);
    }

    public static Task<TResult> UploadAsync<T, TResult>(this HttpRequestMessage request, T provider, Func<T, bool, TResult> done, Func<Exception, TResult> fail)
      where T : MultipartFileStreamProvider
    {
      if (!request.Content.IsMimeMultipartContent())
        return Task.Factory.StartNew(() => fail(null));

      return request.Content.ReadAsMultipartAsync(provider).ContinueWith(x => x.IsFaulted || x.Exception != null ? fail(x.Exception) : done(provider, IsComplete(request.Content.Headers.ContentRange)));
    }

    public static string UnquotedFileName(this ContentDispositionHeaderValue contentDisposition)
    {
      string fileName = contentDisposition != null ? contentDisposition.FileName : null;

      if (string.IsNullOrEmpty(fileName))
        return fileName;

      return Path.GetFileName(fileName).Trim('"');
    }

    public static bool IsComplete(this ContentRangeHeaderValue range)
    {
      return range == null || !range.Length.HasValue || (range.To + 1 >= range.Length);
    }

    private static HttpResponseMessage FailedResponse(HttpRequestMessage request)
    {
      return request.CreateResponse(HttpStatusCode.InternalServerError);
    }

    private static HttpResponseMessage OkResponse(HttpRequestMessage request)
    {
      return request.CreateResponse(HttpStatusCode.OK);
    }

    private class StreamProviderWrapper : MultipartFileStreamProvider
    {
      public StreamProviderWrapper(Func<HttpContent, HttpContentHeaders, Stream> streamHandler)
        : base(_directory)
      {
        _streamHandler = streamHandler;
        _fileData = new Collection<MultipartFileData>();
      }

      public override Stream GetStream(HttpContent parent, HttpContentHeaders headers)
      {
        string fileName = UnquotedFileName(headers.ContentDisposition);

        if (!string.IsNullOrEmpty(fileName))
        {
          _fileData.Add(new MultipartFileData(headers, fileName));
          return _streamHandler(parent, headers);
        }

        // not a valid file or formdata - satisfy the caller
        return new MemoryStream();
      }
      
      public new Collection<MultipartFileData> FileData
      {
        get
        {
          return _fileData;
        }
      }
      
      private readonly Collection<MultipartFileData> _fileData;

      private readonly Func<HttpContent, HttpContentHeaders, Stream> _streamHandler;

      private const string _directory = "/";
    }
  }
}
