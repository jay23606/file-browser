using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace TestProject.Controllers {
    [ApiController]
    [Route("[controller]")]
    public class TestController : ControllerBase {

        private readonly ILogger<TestController> _logger;
        private readonly string _rootDirectory;

        public TestController(ILogger<TestController> logger, IWebHostEnvironment environment) {
            _logger = logger;
            _rootDirectory = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        [HttpGet("browse")]
        public IActionResult BrowseDirectory(string path = "")
        {
            string fullPath = Path.Combine(_rootDirectory, path);
            if (!Directory.Exists(fullPath)) return NotFound("Directory not found");

            var folderInfo = new DirectoryInfo(fullPath);
            var folders = folderInfo.GetDirectories().Select(d => new {
                Name = d.Name,
                DateModified = d.LastWriteTime.ToString("M/d/yyyy h:mm tt"),
                FileCount = d.GetFiles().Length
            });
            var files = folderInfo.GetFiles().Select(f => new {
                Name = f.Name,
                DateModified = f.LastWriteTime.ToString("M/d/yyyy h:mm tt"),
                Size = f.Length
            });

            var result = new
            {
                Folders = folders,
                Files = files
            };

            return Ok(result);
        }

        [HttpGet("search")]
        public IActionResult SearchFiles(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return Ok("[]");
            if (!query.Contains("*")) query = $"*{query}*";
            var files = Directory.GetFiles(_rootDirectory, query, SearchOption.AllDirectories)
                .Select(f => new
                {
                    Name = Path.GetFileName(f),
                    Path = f,
                    Size = new FileInfo(f).Length
                });
            return Ok(JsonSerializer.Serialize(files));
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFiles(List<IFormFile> files, [FromForm] string path = "")
        {
            if (files == null || files.Count == 0)
                return BadRequest(new { message = "No files selected" });

            var targetDir = Path.Combine(_rootDirectory, path);
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            foreach (var file in files)
            {
                var filePath = Path.Combine(targetDir, file.FileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await file.CopyToAsync(stream);
            }

            return Ok(new { message = $"{files.Count} file(s) uploaded successfully" });
        }

        public class DeleteRequest
        {
            public string Path { get; set; }
            public List<ItemToDelete> Items { get; set; }
        }

        public class ItemToDelete
        {
            public string Name { get; set; }
            public string Type { get; set; } // "file" or "folder"
        }

        [HttpPost("delete")]
        public IActionResult Delete([FromBody] DeleteRequest request)
        {
            var targetDir = Path.Combine(_rootDirectory, request.Path ?? "");

            foreach (var item in request.Items)
            {
                try
                {
                    var fullPath = Path.Combine(targetDir, item.Name);
                    if (item.Type == "file" && System.IO.File.Exists(fullPath))
                        System.IO.File.Delete(fullPath);
                    else if (item.Type == "folder" && Directory.Exists(fullPath))
                        Directory.Delete(fullPath, true); 
                }
                catch (Exception ex)
                {
                    return BadRequest(new { error = ex.Message });
                }
            }

            return Ok(new { message = $"{request.Items.Count} item(s) deleted" });
        }

        public class NewFolderRequest
        {
            public string Path { get; set; } = "";
            public string FolderName { get; set; } = "";
        }

        [HttpPost("newfolder")]
        public IActionResult CreateFolder([FromBody] NewFolderRequest request)
        {
            try
            {
                var targetDir = Path.Combine(_rootDirectory, request.Path ?? "");
                var newFolderPath = Path.Combine(targetDir, request.FolderName);

                if (Directory.Exists(newFolderPath))
                    return BadRequest(new { message = "Folder already exists" });

                Directory.CreateDirectory(newFolderPath);

                return Ok(new { message = $"Folder '{request.FolderName}' created" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        public class MoveCopyRequest
        {
            public string SourcePath { get; set; }     // current folder path from pathBox
            public string DestinationPath { get; set; } // target folder path
            public List<ItemDto> Items { get; set; }    // files/folders selected
        }

        public class ItemDto
        {
            public string Name { get; set; }
            public string Type { get; set; } // "file" or "folder"
        }



        [HttpPost("move")]
        public IActionResult Move([FromBody] MoveCopyRequest request)
        {
            try
            {
                
                string sourceDir = string.IsNullOrWhiteSpace(request.SourcePath) || request.SourcePath == "/"
                    ? _rootDirectory
                    : Path.GetFullPath(Path.Combine(_rootDirectory,
                        request.SourcePath.Replace('\\', Path.DirectorySeparatorChar)
                                          .Replace('/', Path.DirectorySeparatorChar)
                                          .TrimStart('.', Path.DirectorySeparatorChar)));

                string destDir = string.IsNullOrWhiteSpace(request.DestinationPath) || request.DestinationPath == "/"
                    ? _rootDirectory
                    : Path.GetFullPath(Path.Combine(_rootDirectory,
                        request.DestinationPath.Replace('\\', Path.DirectorySeparatorChar)
                                               .Replace('/', Path.DirectorySeparatorChar)
                                               .TrimStart('.', Path.DirectorySeparatorChar)));

                // prevent escaping outside root
                if (!sourceDir.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !destDir.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "Invalid path traversal attempt" });

                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                foreach (var item in request.Items)
                {
                    var sourcePath = Path.Combine(sourceDir, item.Name);
                    var destPath = Path.Combine(destDir, item.Name);

                    if (item.Type == "file" && System.IO.File.Exists(sourcePath))
                        System.IO.File.Move(sourcePath, destPath, overwrite: true);
                    else if (item.Type == "folder" && Directory.Exists(sourcePath))
                    {
                        if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
                        Directory.Move(sourcePath, destPath);
                    }
                }
                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("copy")]
        public IActionResult Copy([FromBody] MoveCopyRequest request)
        {
            try
            {
                string sourceDir = string.IsNullOrWhiteSpace(request.SourcePath) || request.SourcePath == "/"
                    ? _rootDirectory
                    : Path.GetFullPath(Path.Combine(_rootDirectory,
                        request.SourcePath.Replace('\\', Path.DirectorySeparatorChar)
                                          .Replace('/', Path.DirectorySeparatorChar)
                                          .TrimStart('.', Path.DirectorySeparatorChar)));

                string destDir = string.IsNullOrWhiteSpace(request.DestinationPath) || request.DestinationPath == "/"
                    ? _rootDirectory
                    : Path.GetFullPath(Path.Combine(_rootDirectory,
                        request.DestinationPath.Replace('\\', Path.DirectorySeparatorChar)
                                               .Replace('/', Path.DirectorySeparatorChar)
                                               .TrimStart('.', Path.DirectorySeparatorChar)));

                if (!sourceDir.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase) ||
                    !destDir.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "Invalid path traversal attempt" });

                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                foreach (var item in request.Items)
                {
                    var sourcePath = Path.Combine(sourceDir, item.Name);
                    var destPath = Path.Combine(destDir, item.Name);

                    if (item.Type == "file" && System.IO.File.Exists(sourcePath))
                        System.IO.File.Copy(sourcePath, destPath, overwrite: true);
                    else if (item.Type == "folder" && Directory.Exists(sourcePath))
                        CopyDirectory(sourcePath, destPath); 
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                System.IO.File.Copy(file, destFile, overwrite: true);
            }

            foreach (var folder in Directory.GetDirectories(sourceDir))
            {
                var destFolder = Path.Combine(destDir, Path.GetFileName(folder));
                CopyDirectory(folder, destFolder);
            }
        }
    }
}