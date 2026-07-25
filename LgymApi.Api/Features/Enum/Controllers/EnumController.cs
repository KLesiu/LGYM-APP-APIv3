using System.Globalization;
using LgymApi.Api.Extensions;
using LgymApi.Api.Features.Common.Contracts;
using LgymApi.Api.Features.Enum.Contracts;
using LgymApi.Application.Platform.ReferenceData.Enums;
using LgymApi.Application.Platform.ReferenceData.Enums.Models;
using LgymApi.Application.Mapping.Core;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace LgymApi.Api.Features.Enum.Controllers;

[ApiController]
[Route("api/enums")]
public sealed class EnumController : ControllerBase
{
    private readonly IEnumService _enumService;
    private readonly IMapper _mapper;

    public EnumController(IEnumService enumService, IMapper mapper)
    {
        _enumService = enumService;
        _mapper = mapper;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<string>), StatusCodes.Status200OK)]
    public IActionResult GetAvailableEnums()
    {
        return Ok(_enumService.GetAvailableEnumTypes());
    }

    [HttpGet("all")]
    [ProducesResponseType(typeof(List<EnumLookupResponseDto>), StatusCodes.Status200OK)]
    public IActionResult GetAllEnumLookups()
    {
        var culture = GetRequestCulture();
        var enumTypes = _enumService.GetAvailableEnumTypes();
        var result = new List<EnumLookupResponseDto>();

        foreach (var enumType in enumTypes)
        {
            var lookup = _enumService.GetLookupByName(enumType, culture);
            if (lookup != null)
            {
                result.Add(_mapper.Map<EnumLookupResponse, EnumLookupResponseDto>(lookup));
            }
        }

        return Ok(result);
    }

    [HttpGet("{enumType}")]
    [ProducesResponseType(typeof(EnumLookupResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ResponseMessageDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEnumLookup([FromRoute] string enumType, CancellationToken cancellationToken)
    {
        var culture = GetRequestCulture();
        var result = await _enumService.GetLookupByNameAsync(enumType, culture, cancellationToken);

        if (result.IsFailure)
        {
            return result.ToActionResult();
        }

        return Ok(_mapper.Map<EnumLookupResponse, EnumLookupResponseDto>(result.Value));
    }

    private CultureInfo GetRequestCulture()
    {
        var feature = HttpContext.Features.Get<IRequestCultureFeature>();
        return feature?.RequestCulture?.UICulture ?? CultureInfo.CurrentUICulture;
    }
}
