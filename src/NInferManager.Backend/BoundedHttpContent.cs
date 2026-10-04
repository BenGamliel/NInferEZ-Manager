using System.Text;

namespace NInferManager.Backend;

internal static class BoundedHttpContent
{
    public static async Task<string> ReadTextAsync(HttpContent content,CancellationToken token,int maximumBytes=1024*1024)
    {
        if(content.Headers.ContentLength>maximumBytes)throw new InvalidDataException("The remote catalog exceeds the safety limit.");
        await using var input=await content.ReadAsStreamAsync(token);
        using var output=new MemoryStream();var buffer=new byte[16384];
        while(true)
        {
            var read=await input.ReadAsync(buffer,token);if(read==0)break;
            if(output.Length+read>maximumBytes)throw new InvalidDataException("The remote catalog exceeds the safety limit.");
            output.Write(buffer,0,read);
        }
        return Encoding.UTF8.GetString(output.GetBuffer(),0,(int)output.Length);
    }
}
