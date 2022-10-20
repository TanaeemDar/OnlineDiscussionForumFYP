using Microsoft.WindowsAzure.Storage.Blob;
using OnlineDisscussionForum.Data;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.WindowsAzure.Storage;

namespace OnlineDisscussionForum.Service
{
    public class UploadService : IUpload
    {
        public CloudBlobContainer GetBlobContainer(string connectionString, string containerName)
        {
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            var blobCLient = storageAccount.CreateCloudBlobClient();
            return blobCLient.GetContainerReference(containerName);
        }
    }
}
