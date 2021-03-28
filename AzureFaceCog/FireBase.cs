using Firebase.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AzureFaceCog
{
    public class FireBase
    {
        //CONSTANT VARIABLES
        const string FireBaseBucket = "nfgcs-investment.appspot.com";
        const string FireBaseSenderKey = "AAAABR5OLho:APA91bHPhQuColTh2SV8dGFFK4xesfkkfxaAm0irRKw7HzAClKxkb47SCkDGtcYg7dmtGVR4o8EZlgaKH2ReeZoLqOMOEwEIZC65vfw9PvfiDK0gl1seZJX8vbclqs_hi5OV4lvy9UCI";
        const string RawHash = "6f9dff5af05096ea9f23cc7bedd65683";

        public static async Task<string> UploadDocumentAsync(string fileExtension, string imageName, string profilePhotoPath, string mimeType = "image/jpeg")
        {
            try
            {
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

                CancellationToken token = cancellationTokenSource.Token;

                //var bytes = Convert.FromBase64String(document.File);

                using (var stream = new FileStream(profilePhotoPath, FileMode.Open)) { 

                    var task = new FirebaseStorage(FireBaseBucket)
                    .Child("fcae-cogn/root")
                    .Child("Test")
                    .Child(fileExtension)
                    .PutAsync(stream, token, mimeType);

                    task.Progress.ProgressChanged += (s, e) => Console.WriteLine($"Progress: {e.Percentage} %");

                    var result = await task;
                    return result;                   
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return null;
            }
        }

        private static string GetUniqueFileName(string fileName)
        {
            fileName = Path.GetFileName(fileName);
            return Path.GetFileNameWithoutExtension(fileName) + "_" + Guid.NewGuid().ToString().Substring(5, 5) + Path.GetExtension(fileName);
        }
    }
}

public class FileDocument
{
    private string _Path { get; set; }

    private string _File { get; set; }

    private string _Name { get; set; }

    private FileDocumentType _Type { get; set; }

    public string Path => _Path;

    public string File => _File;

    public string Name => _Name;

    public string FileNameWithExtension => $"{_Name}{_Type.Extension}";

    public FileDocumentType DocumentType => _Type;

    private FileDocument() { }

    private FileDocument(string File) { _File = File; }

    private FileDocument(string File, string Name) { File = File; Name = Name; }

    private FileDocument(string File, string Name, string Path) { File = File; Name = Name; _Path = Path; }

    private FileDocument(string File, string Name, string Path, FileDocumentType Type) { File = File; Name = Name; Path = Path; Type = Type; }

    public static FileDocument Create() => new FileDocument();

    public static FileDocument Create(string File) => new FileDocument(File);

    public static FileDocument Create(string File, string Name) => new FileDocument(File, Name);

    public static FileDocument Create(string File, string Name, string Path) => new FileDocument(File, Name, Path);

    public static FileDocument Create(string File, string Name, string Path, FileDocumentType Type) => new FileDocument(File, Name, Path, Type);
}

public class FileDocumentType
{
    public string Extension { get; set; }

    public string MimeType { get; set; }

    public static FileDocumentType GetDocumentType(string type)
    {
        return new FileDocumentType { Extension = ".jpg", MimeType = type };
    }
}

public class MIMETYPE
{
    public static string IMAGE => "image/jpeg";
}

public class AppSettings
{

    public string UploadDrive { get; set; }

    public string DriveName { get; set; }

    public string FireBaseBucket { get; set; }

    public string FireBaseSenderKey { get; set; }

    public string RawHash { get; set; }

}