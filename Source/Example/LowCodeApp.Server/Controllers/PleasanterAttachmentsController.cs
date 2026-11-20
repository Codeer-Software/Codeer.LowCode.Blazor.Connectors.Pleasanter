using Codeer.LowCode.Blazor.Connectors.Pleasanter;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using LowCodeApp.Server.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LowCodeApp.Server.Controllers
{
    //プリザンターの添付のフィールドのダウンロード。読めるかの判定 (レコード・フィールドの権限・そのレコードの添付か) は GetAttachmentAsync が持つ
    [Authorize, AutoValidateAntiforgeryToken]
    [ApiController]
    public class PleasanterAttachmentsController : ControllerBase, IAsyncDisposable
    {
        readonly DataService _dataService;
        readonly AuditContext _audit;

        public PleasanterAttachmentsController(DataService dataService, AuditContext audit)
        {
            _dataService = dataService;
            _audit = audit;
        }

        [NonAction]
        public async ValueTask DisposeAsync()
            => await _dataService.DisposeAsync();

        [HttpGet(PleasanterAttachmentsFieldDesign.DownloadUrl), Audit(AuditCategory.Export)]
        public async Task<IActionResult> DownloadAsync(string? module, string? id, string? field, string? guid)
        {
            _audit.AddTarget(module ?? string.Empty, id, $"PleasanterAttachment:{field}");
            var file = await _dataService.Pleasanter.GetAttachmentAsync(_dataService.ModuleDataIO, _dataService.Design.DesignData, module, id, field, guid);
            if (file == null) return NotFound();
            //ブラウザで開かせない (保存させる)。ファイル名は File が Content-Disposition に符号化して入れる
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(file.Content, "application/octet-stream", SafeFileName(file.FileName));
        }

        //パスの区切り・制御文字を外す (名前はプリザンターに保存された利用者の入力)
        static string SafeFileName(string name)
        {
            var chars = name.Select(c => char.IsControl(c) || c is '/' or '\\' or '"' ? '_' : c).ToArray();
            var safe = new string(chars).Trim().Trim('.');
            return string.IsNullOrEmpty(safe) ? "download" : safe;
        }
    }
}
