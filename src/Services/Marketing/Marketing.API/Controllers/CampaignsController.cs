﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopOnContainers.Services.Marketing.API.Dto;
using Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure;
using Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure.Repositories;
using Microsoft.eShopOnContainers.Services.Marketing.API.Infrastructure.Services;
using Microsoft.eShopOnContainers.Services.Marketing.API.Model;
using Microsoft.eShopOnContainers.Services.Marketing.API.ViewModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.eShopOnContainers.Services.Marketing.API.Controllers
{

    [Route("api/v1/[controller]")]
    [Authorize]
    public class CampaignsController : Controller
    {
        private readonly MarketingContext _context;
        private readonly MarketingSettings _settings;
        private readonly IMarketingDataRepository _marketingDataRepository;
        private readonly IIdentityService _identityService;
        private readonly ILogger<CampaignsController> _logger;

        public CampaignsController(MarketingContext context,
            IMarketingDataRepository marketingDataRepository,
             IOptionsSnapshot<MarketingSettings> settings,
            IIdentityService identityService,
            ILogger<CampaignsController> logger)
        {
            _context = context;
            _marketingDataRepository = marketingDataRepository;
            _settings = settings.Value;
            _identityService = identityService;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<CampaignDTO>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetAllCampaigns()
        {
            _logger.LogTrace("Get all campaigns");
            var campaignList = await _context.Campaigns
                .ToListAsync();

            if (campaignList is null)
            {
                return Ok();
            }

            var campaignDtoList = MapCampaignModelListToDtoList(campaignList);

            return Ok(campaignDtoList);
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(CampaignDTO), (int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        public async Task<IActionResult> GetCampaignById(int id)
        {
            _logger.LogTrace("Get campaign by id {id}", id);
            var campaign = await _context.Campaigns
                .SingleOrDefaultAsync(c => c.Id == id);

            if (campaign is null)
            {
                return NotFound();
            }

            var campaignDto = MapCampaignModelToDto(campaign);

            return Ok(campaignDto);
        }

        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.Created)]
        public async Task<IActionResult> CreateCampaign([FromBody] CampaignDTO campaignDto)
        {
            if (campaignDto is null)
            {
                return BadRequest();
            }

            var campaign = MapCampaignDtoToModel(campaignDto);

            await _context.Campaigns.AddAsync(campaign);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCampaignById), new { id = campaign.Id }, null);
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType((int)HttpStatusCode.Created)]
        public async Task<IActionResult> UpdateCampaign(int id, [FromBody] CampaignDTO campaignDto)
        {
            if (id < 1 || campaignDto is null)
            {
                return BadRequest();
            }

            var campaignToUpdate = await _context.Campaigns.FindAsync(id);
            if (campaignToUpdate is null)
            {
                return NotFound();
            }

            campaignToUpdate.Name = campaignDto.Name;
            campaignToUpdate.Description = campaignDto.Description;
            campaignToUpdate.From = campaignDto.From;
            campaignToUpdate.To = campaignDto.To;
            campaignToUpdate.PictureUri = campaignDto.PictureUri;

            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCampaignById), new { id = campaignToUpdate.Id }, null);
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType((int)HttpStatusCode.NoContent)]
        public async Task<IActionResult> Delete(int id)
        {
            if (id < 1)
            {
                return BadRequest();
            }

            var campaignToDelete = await _context.Campaigns.FindAsync(id);
            if (campaignToDelete is null)
            {
                return NotFound();
            }

            _context.Campaigns.Remove(campaignToDelete);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpGet("user")]
        [ProducesResponseType(typeof(PaginatedItemsViewModel<CampaignDTO>), (int)HttpStatusCode.OK)]
        public async Task<IActionResult> GetCampaignsByUserId( int pageSize = 10, int pageIndex = 0)
        {
            var userId = _identityService.GetUserIdentity();
            _logger.LogTrace("Get campaigns by userId {userId}", userId);

            var marketingData = await _marketingDataRepository.GetAsync(userId.ToString());

            var campaignDtoList = new List<CampaignDTO>();
            
            if (marketingData != null)
            {
                var locationIdCandidateList = marketingData.Locations.Select(x => x.LocationId);
                var userCampaignList = await _context.Rules
                    .OfType<UserLocationRule>()
                    .Include(c => c.Campaign)
                    .Where(c => c.Campaign.From <= DateTime.Now
                                && c.Campaign.To >= DateTime.Now
                                && locationIdCandidateList.Contains(c.LocationId))
                                    .Select(c => c.Campaign)
                                    .ToListAsync();

                if (userCampaignList != null && userCampaignList.Any())
                {
                    var userCampaignDtoList = MapCampaignModelListToDtoList(userCampaignList);
                    campaignDtoList.AddRange(userCampaignDtoList);
                }
                
            }

            var totalItems = campaignDtoList.Count();
            campaignDtoList = campaignDtoList
                .Skip(pageSize * pageIndex)
                .Take(pageSize).ToList();

            var model = new PaginatedItemsViewModel<CampaignDTO>(
                pageIndex, pageSize, totalItems, campaignDtoList);

            return Ok(model);
        }


        private List<CampaignDTO> MapCampaignModelListToDtoList(List<Campaign> campaignList)
        {
            var campaignDtoList = new List<CampaignDTO>();

            campaignList.ForEach(campaign => campaignDtoList
                .Add(MapCampaignModelToDto(campaign)));

            return campaignDtoList;
        }

        private CampaignDTO MapCampaignModelToDto(Campaign campaign)
        {
            var userId = _identityService.GetUserIdentity();
            var dto = new CampaignDTO
            {
                Id = campaign.Id,
                Name = campaign.Name,
                Description = campaign.Description,
                From = campaign.From,
                To = campaign.To,
                PictureUri = GetUriPlaceholder(campaign),
            };

            if (!string.IsNullOrEmpty(_settings.CampaignDetailFunctionUri))
            {
                dto.DetailsUri = $"{_settings.CampaignDetailFunctionUri}&campaignId={campaign.Id}&userId={userId}";
            }

            return dto;
        }

        private Campaign MapCampaignDtoToModel(CampaignDTO campaignDto)
        {
            return new Campaign
            {
                Id = campaignDto.Id,
                Name = campaignDto.Name,
                Description = campaignDto.Description,
                From = campaignDto.From,
                To = campaignDto.To,
                PictureUri = campaignDto.PictureUri
            };
        }

        private string GetUriPlaceholder(Campaign campaign)
        {
            var baseUri = _settings.PicBaseUrl;

            return _settings.AzureStorageEnabled
                    ? baseUri + campaign.PictureName
                    : baseUri.Replace("[0]", campaign.Id.ToString());
        }
    }
}