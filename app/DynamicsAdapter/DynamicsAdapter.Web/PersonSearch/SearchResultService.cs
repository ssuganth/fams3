﻿using AutoMapper;
using DynamicsAdapter.Web.PersonSearch.Models;
using Fams3Adapter.Dynamics.Address;
using Fams3Adapter.Dynamics.Identifier;
using Fams3Adapter.Dynamics.Name;
using Fams3Adapter.Dynamics.Person;
using Fams3Adapter.Dynamics.PhoneNumber;
using Fams3Adapter.Dynamics.SearchRequest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicsAdapter.Web.PersonSearch
{
    public interface ISearchResultService
    {
        Task<bool> ProcessPersonFound(Person person, ProviderProfile providerProfile, SSG_SearchRequest searchRequest, CancellationToken cancellationToken);
    }

    public class SearchResultService : ISearchResultService
    {
        private readonly ILogger<SearchResultService> _logger;
        private readonly ISearchRequestService _searchRequestService;
        private readonly IMapper _mapper;

        public SearchResultService(ISearchRequestService searchRequestService,ILogger<SearchResultService> logger, IMapper mapper)
        {
            _searchRequestService = searchRequestService;
            _logger = logger;
            _mapper = mapper;
        }

        public async Task<bool> ProcessPersonFound(Person person, ProviderProfile providerProfile, SSG_SearchRequest request, CancellationToken concellationToken)
        {
            if (person == null) return true;
            
            int? providerDynamicsID  = providerProfile.DynamicsID();
            PersonEntity ssg_person = _mapper.Map<PersonEntity>(person);
            ssg_person.SearchRequest = request;
            ssg_person.InformationSource = providerDynamicsID;
            _logger.LogDebug($"Attempting to create a person entity for SearchRequest[{request.SearchRequestId}]");
            SSG_Person returnedPerson = await _searchRequestService.SavePerson(ssg_person, concellationToken);
            _logger.LogInformation($"Successfully created person entity for SearchRequest [{request.SearchRequestId}]");

            _logger.LogDebug($"Attempting to creat identifier entities for SearchRequest[{request.SearchRequestId}]");
            await UploadIdentifiers(person, request, returnedPerson, providerDynamicsID, concellationToken);

            _logger.LogDebug($"Attempting to creat address entities for SearchRequest[{request.SearchRequestId}]");
            await UploadAddresses(person, request, returnedPerson, providerDynamicsID, concellationToken);

            _logger.LogDebug($"Attempting to creat phonenumber entities for SearchRequest[{request.SearchRequestId}]");
            await UploadPhoneNumbers(person, request, returnedPerson, providerDynamicsID, concellationToken);

            _logger.LogDebug($"Attempting to creat name entities for SearchRequest[{request.SearchRequestId}]");
            await UploadNames(person, request, returnedPerson, providerDynamicsID, concellationToken);
            return true;

        }

        private async Task<bool> UploadIdentifiers(Person person, SSG_SearchRequest request, SSG_Person ssg_person, int? providerDynamicsID, CancellationToken concellationToken)
        {
            if (person.Identifiers == null) return true;
            try
            {
                foreach (var matchFoundPersonId in person.Identifiers)
                {
                    SSG_Identifier identifier = _mapper.Map<SSG_Identifier>(matchFoundPersonId);
                    identifier.SearchRequest = request;
                    identifier.InformationSource = providerDynamicsID;
                    identifier.Person = ssg_person;
                    var identifer = await _searchRequestService.CreateIdentifier(identifier, concellationToken);
                }
                return true;
            }catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return false;
            }
        }

        private async Task<bool> UploadAddresses(Person person, SSG_SearchRequest request, SSG_Person ssg_person, int? providerDynamicsID, CancellationToken concellationToken)
        {
            if (person.Addresses == null) return true;
            try
            {
                foreach (var address in person.Addresses)
                {
                    SSG_Address addr = _mapper.Map<SSG_Address>(address);
                    addr.SearchRequest = request;
                    addr.InformationSource = providerDynamicsID;
                    addr.Person = ssg_person;
                    var uploadedAddr = await _searchRequestService.CreateAddress(addr, concellationToken);
                }
                return true;
            }catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return false;
            }
}

        private async Task<bool> UploadPhoneNumbers(Person person, SSG_SearchRequest request, SSG_Person ssg_person, int? providerDynamicsID, CancellationToken concellationToken)
        {
            if (person.Phones == null) return true;
            try { 
                foreach (var phone in person.Phones)
                {
                    SSG_PhoneNumber ph = _mapper.Map<SSG_PhoneNumber>(phone);
                    ph.SearchRequest = request;
                    ph.InformationSource = providerDynamicsID;
                    ph.Person = ssg_person;
                    await _searchRequestService.CreatePhoneNumber(ph, concellationToken);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return false;
            }
        }

        private async Task<bool> UploadNames(Person person, SSG_SearchRequest request, SSG_Person ssg_person, int? providerDynamicsID, CancellationToken concellationToken)
        {
            if (person.Names == null) return true;
            try
            {
                foreach (var name in person.Names)
                {
                    SSG_Aliase n = _mapper.Map<SSG_Aliase>(name);
                    n.SearchRequest = request;
                    n.InformationSource = providerDynamicsID;
                    n.Person = ssg_person;
                    await _searchRequestService.CreateName(n, concellationToken);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return false;
            }
        }
    }
}
