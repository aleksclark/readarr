using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.MediaFiles.BookImport;
using Readarr.Http;

namespace Readarr.Api.V1.ManualImport
{
    [V1ApiController("manualimport/collection")]
    public class CollectionImportController : Controller
    {
        private readonly ICollectionPreviewService _collectionPreviewService;
        private readonly Logger _logger;

        public CollectionImportController(ICollectionPreviewService collectionPreviewService,
                                          Logger logger)
        {
            _collectionPreviewService = collectionPreviewService;
            _logger = logger;
        }

        [HttpPost]
        public ActionResult<List<CollectionImportResource>> Preview([FromBody] CollectionImportRequestResource request)
        {
            if (string.IsNullOrWhiteSpace(request?.Path))
            {
                return BadRequest("Path is required");
            }

            _logger.Debug("Collection import preview requested for path: {0}", request.Path);

            var items = _collectionPreviewService.GetCollectionPreview(request.Path);

            return Ok(items.ToResource());
        }
    }
}
