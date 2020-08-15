﻿using AutoMapper;
using DynamicsAdapter.Web.SearchAgency.Models;
using Fams3Adapter.Dynamics.Address;
using Fams3Adapter.Dynamics.Employment;
using Fams3Adapter.Dynamics.Identifier;
using Fams3Adapter.Dynamics.Notes;
using Fams3Adapter.Dynamics.Person;
using Fams3Adapter.Dynamics.PhoneNumber;
using Fams3Adapter.Dynamics.RelatedPerson;
using Fams3Adapter.Dynamics.SearchRequest;
using Fams3Adapter.Dynamics.Types;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace DynamicsAdapter.Web.SearchAgency
{
    public interface IAgencyRequestService
    {
        Task<SSG_SearchRequest> ProcessSearchRequestOrdered(SearchRequestOrdered searchRequestOrdered);
        Task<SSG_SearchRequest> ProcessCancelSearchRequest(SearchRequestOrdered cancelSearchRequest);
        Task<SSG_SearchRequest> ProcessUpdateSearchRequest(SearchRequestOrdered updateSearchRequest);
    }

    public class AgencyRequestService : IAgencyRequestService
    {
        private readonly ILogger<AgencyRequestService> _logger;
        private readonly ISearchRequestService _searchRequestService;
        private readonly IMapper _mapper;
        private Person _personSought;
        private SSG_Person _uploadedPerson;
        private SSG_SearchRequest _uploadedSearchRequest;
        private CancellationToken _cancellationToken;

        public AgencyRequestService(ISearchRequestService searchRequestService, ILogger<AgencyRequestService> logger, IMapper mapper)
        {
            _searchRequestService = searchRequestService;
            _logger = logger;
            _mapper = mapper;
            _personSought = null;
            _uploadedPerson = null;
            _uploadedSearchRequest = null;
        }

        public async Task<SSG_SearchRequest> ProcessSearchRequestOrdered(SearchRequestOrdered searchRequestOrdered)
        {
            _personSought = searchRequestOrdered.Person;
            var cts = new CancellationTokenSource();
            _cancellationToken = cts.Token;

            SearchRequestEntity searchRequestEntity = _mapper.Map<SearchRequestEntity>(searchRequestOrdered);
            searchRequestEntity.CreatedByApi = true;
            searchRequestEntity.SendNotificationOnCreation = true;
            _uploadedSearchRequest = await _searchRequestService.CreateSearchRequest(searchRequestEntity, cts.Token);
            _logger.LogInformation("Create Search Request successfully");

            PersonEntity personEntity = _mapper.Map<PersonEntity>(_personSought);
            personEntity.SearchRequest = _uploadedSearchRequest;
            personEntity.InformationSource = InformationSourceType.Request.Value;
            _uploadedPerson = await _searchRequestService.SavePerson(personEntity, _cancellationToken);
            _logger.LogInformation("Create Person successfully");

            await UploadIdentifiers();
            await UploadAddresses();
            await UploadPhones();
            await UploadEmployment();
            await UploadRelatedPersons();
            return _uploadedSearchRequest;
        }

        public async Task<SSG_SearchRequest> ProcessCancelSearchRequest(SearchRequestOrdered searchRequestOrdered)
        {
            var cts = new CancellationTokenSource();
            _cancellationToken = cts.Token;
            SSG_SearchRequest ssgSearchRequest = await _searchRequestService.GetSearchRequest(searchRequestOrdered.SearchRequestKey, _cancellationToken);
            if (ssgSearchRequest == null)
            {
                _logger.LogInformation("the cancelling search request does not exist.");
                return null;
            }
            return await _searchRequestService.CancelSearchRequest(searchRequestOrdered.SearchRequestKey, _cancellationToken);
        }

        public async Task<SSG_SearchRequest> ProcessUpdateSearchRequest(SearchRequestOrdered searchRequestOrdered)
        {
            var cts = new CancellationTokenSource();
            _cancellationToken = cts.Token;
            SSG_SearchRequest existedSearchRequest = await _searchRequestService.GetSearchRequest(searchRequestOrdered.SearchRequestKey, _cancellationToken);
            if (existedSearchRequest == null)
            {
                _logger.LogInformation("the updating search request does not exist.");
                return null;
            }
            SearchRequestEntity newSearchRequest = _mapper.Map<SearchRequestEntity>(searchRequestOrdered);
            _uploadedSearchRequest = await UpdateSearchRequest(existedSearchRequest, newSearchRequest);
            _logger.LogInformation("Update Search Request successfully");

            if (!String.IsNullOrEmpty(newSearchRequest.Notes)
                && !String.Equals(existedSearchRequest.Notes, newSearchRequest.Notes, StringComparison.InvariantCultureIgnoreCase))
            {
                await UploadNotes(newSearchRequest);
                _logger.LogInformation("Create Notes successfully");
            }

            _personSought = searchRequestOrdered.Person;
            PersonEntity newPersonEntity = _mapper.Map<PersonEntity>(_personSought);
            _uploadedPerson = await UpdatePersonSought(newPersonEntity, existedSearchRequest);
            _logger.LogInformation("Update Person successfully");


            await UpdateRelatedPerson(existedSearchRequest);
            await UpdateRelatedApplicant( new RelatedPersonEntity()
                                            {
                                                FirstName = newSearchRequest.ApplicantFirstName,
                                                LastName = newSearchRequest.ApplicantLastName,
                                                StatusCode = 1
                                            }, 
                                            existedSearchRequest);

            await UpdateEmployment(existedSearchRequest);

            return _uploadedSearchRequest;
        }

        private async Task<bool> UploadIdentifiers()
        {
            if (_personSought.Identifiers == null) return true;
            _logger.LogDebug($"Attempting to create identifier records for SearchRequest.");

            foreach (var personId in _personSought.Identifiers.Where(m => m.Owner == OwnerType.PersonSought))
            {
                IdentifierEntity identifier = _mapper.Map<IdentifierEntity>(personId);
                identifier.SearchRequest = _uploadedSearchRequest;
                identifier.InformationSource = InformationSourceType.Request.Value;
                identifier.Person = _uploadedPerson;
                SSG_Identifier newIdentifier = await _searchRequestService.CreateIdentifier(identifier, _cancellationToken);
            }
            _logger.LogInformation("Create identifier records for SearchRequest successfully");
            return true;
        }

        private async Task<bool> UploadAddresses()
        {
            if (_personSought.Addresses == null) return true;

            _logger.LogDebug($"Attempting to create adddress for SoughtPerson");

            foreach (var address in _personSought.Addresses.Where(m => m.Owner == OwnerType.PersonSought))
            {
                AddressEntity addr = _mapper.Map<AddressEntity>(address);
                addr.SearchRequest = _uploadedSearchRequest;
                addr.InformationSource = InformationSourceType.Request.Value;
                addr.Person = _uploadedPerson;
                SSG_Address uploadedAddr = await _searchRequestService.CreateAddress(addr, _cancellationToken);
            }
            _logger.LogInformation("Create addresses records for SearchRequest successfully");
            return true;
        }

        private async Task<bool> UploadPhones()
        {
            if (_personSought.Phones == null) return true;

            _logger.LogDebug($"Attempting to create Phones for SoughtPerson");

            foreach (var phone in _personSought.Phones.Where(m => m.Owner == OwnerType.PersonSought))
            {
                PhoneNumberEntity ph = _mapper.Map<PhoneNumberEntity>(phone);
                ph.SearchRequest = _uploadedSearchRequest;
                ph.InformationSource = InformationSourceType.Request.Value;
                ph.Person = _uploadedPerson;
                SSG_PhoneNumber uploadedPhone = await _searchRequestService.CreatePhoneNumber(ph, _cancellationToken);
            }
            _logger.LogInformation("Create phones records for SearchRequest successfully");
            return true;
        }

        private async Task<bool> UploadEmployment()
        {
            if (_personSought.Employments == null) return true;

            _logger.LogDebug($"Attempting to create employment records for PersonSought.");

            foreach (var employment in _personSought.Employments)
            {
                EmploymentEntity e = _mapper.Map<EmploymentEntity>(employment);
                e.SearchRequest = _uploadedSearchRequest;
                e.InformationSource = InformationSourceType.Request.Value;
                e.Person = _uploadedPerson;
                SSG_Employment ssg_employment = await _searchRequestService.CreateEmployment(e, _cancellationToken);

                if (employment.Employer != null)
                {
                    foreach (var phone in employment.Employer.Phones)
                    {
                        EmploymentContactEntity p = _mapper.Map<EmploymentContactEntity>(phone);
                        p.Employment = ssg_employment;
                        await _searchRequestService.CreateEmploymentContact(p, _cancellationToken);
                    }
                }
            }

            _logger.LogInformation("Create employment records for SearchRequest successfully");
            return true;
        }

        private async Task<bool> UploadRelatedPersons()
        {
            if (_personSought.RelatedPersons == null) return true;

            _logger.LogDebug($"Attempting to create related person records person sought.");

            foreach (var relatedPerson in _personSought.RelatedPersons)
            {
                RelatedPersonEntity n = _mapper.Map<RelatedPersonEntity>(relatedPerson);
                n.SearchRequest = _uploadedSearchRequest;
                n.InformationSource = InformationSourceType.Request.Value;
                n.Person = _uploadedPerson;
                SSG_Identity relate = await _searchRequestService.CreateRelatedPerson(n, _cancellationToken);

            }
            _logger.LogInformation("Create RelatedPersons records for SearchRequest successfully");
            return true;
        }

        private async Task<SSG_SearchRequest> UpdateSearchRequest(SSG_SearchRequest originalSR, SearchRequestEntity newSR)
        {
            string originNotes = originalSR.Notes;
            SSG_SearchRequest clonedSR = originalSR.Clone();
            SSG_SearchRequest ssgMerged = (SSG_SearchRequest)MergeObj(clonedSR, newSR);
            if (!String.Equals(originNotes, newSR.Notes, StringComparison.InvariantCultureIgnoreCase))
            {
                ssgMerged.Notes = originNotes;
            }
            ssgMerged.SSG_Persons = null;
            ssgMerged.SSG_RelatedPersons = null;
            ssgMerged.SSG_Employments = null;
            return await _searchRequestService.UpdateSearchRequest(ssgMerged, _cancellationToken);

        }

        private async Task<SSG_Person> UpdatePersonSought(PersonEntity personEntity, SSG_SearchRequest existedSearchRequest)
        {
            SSG_Person originalSoughtPerson = existedSearchRequest?.SSG_Persons?.FirstOrDefault(
                m => m.FirstName == existedSearchRequest.PersonSoughtFirstName
                && m.LastName == existedSearchRequest.PersonSoughtLastName
                && m.InformationSource == InformationSourceType.Request.Value);
            if (originalSoughtPerson == null) return null;

            SSG_Person ssgMerged = (SSG_Person)MergeObj(originalSoughtPerson.Clone(), personEntity);
            ssgMerged.SearchRequest = existedSearchRequest;
            return await _searchRequestService.UpdatePerson(ssgMerged, _cancellationToken);

        }

        private async Task<bool> UpdateRelatedPerson(SSG_SearchRequest existedSearchRequest)
        {
            if (_personSought.RelatedPersons == null) return true;

            //update or add relation relatedPerson
            SSG_Identity originalRelatedPerson= existedSearchRequest?.SSG_RelatedPersons?.FirstOrDefault(
            m => m.InformationSource == InformationSourceType.Request.Value && m.PersonType == RelatedPersonPersonType.Relation.Value);

            if (_personSought.RelatedPersons?.Count() > 0)
            {
                RelatedPersonEntity n = _mapper.Map<RelatedPersonEntity>(_personSought.RelatedPersons.ElementAt(0));
                if (originalRelatedPerson == null)
                {
                    await UploadRelatedPersons();
                }
                else
                {
                    SSG_Identity ssgMerged = (SSG_Identity)MergeObj(originalRelatedPerson.Clone(), n);
                    ssgMerged.SearchRequest = _uploadedSearchRequest;
                    ssgMerged.InformationSource = InformationSourceType.Request.Value;
                    ssgMerged.Person = _uploadedPerson;
                    await _searchRequestService.UpdateRelatedPerson(ssgMerged, _cancellationToken);
                    _logger.LogInformation("Update RelatedPersons records for SearchRequest successfully");
                }
            }
            return true;
        }

        private async Task<bool> UpdateRelatedApplicant(RelatedPersonEntity newApplicantEntity, SSG_SearchRequest existedSearchRequest)
        {
            if (newApplicantEntity == null) return true;

            //update or add relation relatedPerson
            SSG_Identity originalRelatedApplicant = existedSearchRequest?.SSG_RelatedPersons?.FirstOrDefault(
            m => m.InformationSource == InformationSourceType.Request.Value && m.PersonType == RelatedPersonPersonType.Applicant.Value);


            if (originalRelatedApplicant == null)
            {
                newApplicantEntity.SearchRequest = _uploadedSearchRequest;
                newApplicantEntity.InformationSource = InformationSourceType.Request.Value;
                newApplicantEntity.Person = _uploadedPerson;
                await _searchRequestService.CreateRelatedPerson(newApplicantEntity, _cancellationToken);
                _logger.LogInformation("Create Related Applicant for SearchRequest successfully");
            }
            else
            {
                SSG_Identity ssgMerged = (SSG_Identity)MergeObj(originalRelatedApplicant.Clone(), newApplicantEntity);
                ssgMerged.SearchRequest = _uploadedSearchRequest;
                ssgMerged.InformationSource = InformationSourceType.Request.Value;
                ssgMerged.Person = _uploadedPerson;
                await _searchRequestService.UpdateRelatedPerson(ssgMerged, _cancellationToken);
                _logger.LogInformation("Update Related Applicant records for SearchRequest successfully");
            }

           

            return true;
        }

        private async Task<bool> UpdateEmployment(SSG_SearchRequest existedSearchRequest)
        {
            if (_personSought.Employments == null) return true;

            _logger.LogDebug($"Attempting to update employment records for PersonSought.");

            SSG_Employment originalEmployment = existedSearchRequest?.SSG_Employments?.FirstOrDefault(
                    m => m.InformationSource == InformationSourceType.Request.Value );

            if (_personSought.Employments.Count() > 0)
            {
                EmploymentEntity employ = _mapper.Map<EmploymentEntity>(_personSought.Employments.ElementAt(0));
                if (originalEmployment == null)
                {
                    await UploadEmployment();
                }
                else
                {
                    SSG_Employment ssgMerged = (SSG_Employment)MergeObj(originalEmployment.Clone(), employ);
                    ssgMerged.SearchRequest = _uploadedSearchRequest;
                    ssgMerged.InformationSource = InformationSourceType.Request.Value;
                    ssgMerged.Person = _uploadedPerson;
                    SSG_Employment newEmployment = await _searchRequestService.UpdateEmployment(ssgMerged, _cancellationToken);
                    newEmployment.IsDuplicated = true;
                    _logger.LogInformation("Update Employment records for SearchRequest successfully");

                    Employer employer = _personSought.Employments.ElementAt(0).Employer;
                    if (employer != null)
                    {
                        foreach (var phone in employer.Phones)
                        {
                            EmploymentContactEntity p = _mapper.Map<EmploymentContactEntity>(phone);
                            p.Employment = newEmployment;
                            await _searchRequestService.CreateEmploymentContact(p, _cancellationToken);
                        }
                    }
                }
            }
            return true;
        }
        private async Task<bool> UploadNotes(SearchRequestEntity newSearchRequestEntity)
        {
            NotesEntity note = new NotesEntity
            {
                StatusCode = 1,
                Description = newSearchRequestEntity.Notes,
                InformationSource = InformationSourceType.Request.Value,
                SearchRequest = _uploadedSearchRequest
            };
            SSG_Notese ssgNote = await _searchRequestService.CreateNotes(note, _cancellationToken);

            if (ssgNote == null)
            {
                _logger.LogError("Create new notes failed.");
                return false;
            }
            _logger.LogInformation("Create new notes successfully.");
            return true;
        }

        private object MergeObj(object originObj, object newObj)
        {
            if (newObj == null) return originObj;
            if (originObj == null) return null;

            object returnedObj = originObj;
            Type newType = newObj.GetType();
            IList<PropertyInfo> props = new List<PropertyInfo>(newType.GetProperties());
            foreach (PropertyInfo propertyInfo in props)
            {
                object newValue = propertyInfo.GetValue(newObj, null);
                if (newValue != null)
                {
                    if (propertyInfo.PropertyType.Name == "Boolean")
                    {
                        if ((bool)newValue != false) //new value is null or false, no matter old value has value or not, we do not change the old value
                        {
                            propertyInfo.SetValue(returnedObj, newValue);
                        }
                    }
                    else if (propertyInfo.PropertyType.Name == "String")
                    {
                        if (!String.IsNullOrEmpty((String)newValue))//new value is null, no matter old value has value or not, we do not change the old value
                        {
                            propertyInfo.SetValue(returnedObj, newValue);
                        }
                    }
                    //else if (propertyInfo.PropertyType.IsClass)
                    //{
                    //    object originRef = propertyInfo.GetValue(returnedObj, null);
                    //    propertyInfo.SetValue(returnedObj, MergeObj(originRef, newValue));
                    //}
                    else
                    {
                        propertyInfo.SetValue(returnedObj, newValue);
                    }
                }
            }
            return returnedObj;
        }


    }

    public static class ObjExtensions
    {
        public static T Clone<T>(this T source)
        {
            var serialized = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<T>(serialized);
        }
    }
}
