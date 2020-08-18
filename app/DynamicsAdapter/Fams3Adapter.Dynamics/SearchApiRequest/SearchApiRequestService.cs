﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fams3Adapter.Dynamics.DataProvider;
using Fams3Adapter.Dynamics.SearchApiEvent;
using Simple.OData.Client;

using Entry = System.Collections.Generic.Dictionary<string, object>;


namespace Fams3Adapter.Dynamics.SearchApiRequest
{

    public interface ISearchApiRequestService
    {
        Task<IEnumerable<SSG_SearchApiRequest>> GetAllReadyForSearchAsync(CancellationToken cancellationToken);

        Task<IEnumerable<SSG_SearchApiRequest>> GetAllValidFailedSearchRequest(CancellationToken cancellationToken);

        Task<IEnumerable<SSG_DataProvider>> GetDataProvidersList(CancellationToken cancellationToken);


        Task<Guid> GetLinkedSearchRequestIdAsync(Guid searchApiRequestId,
            CancellationToken cancellationToken);

        Task<SSG_SearchApiRequest> MarkInProgress(Guid searchApiRequestId, CancellationToken cancellationToken);

        Task<SSG_SearchApiEvent> AddEventAsync(Guid searchApiRequestId, SSG_SearchApiEvent searchApiEvent,
            CancellationToken cancellationToken);

        Task<SSG_SearchApiRequest> MarkComplete(Guid searchApiRequestId, CancellationToken cancellationToken);
        


    }

    /// <summary>
    /// The SearchRequestService expose Search Request Functionality
    /// </summary>
    public class SearchApiRequestService : ISearchApiRequestService
    {

        private readonly IODataClient _oDataClient;
        public SearchApiRequestService(IODataClient oDataClient)
        {
            this._oDataClient = oDataClient;
        }

        /// <summary>
        /// Gets all the search request in `Ready for Search` status
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IEnumerable<SSG_SearchApiRequest>> GetAllReadyForSearchAsync(
            CancellationToken cancellationToken)
        {
            int readyForSearchCode = SearchApiRequestStatusReason.ReadyForSearch.Value;
            List<SSG_SearchApiRequest> results = new List<SSG_SearchApiRequest>();

            //todo: we need to change to use following code, but ODataClient 4 has problems with expand, curent implemented code is a workaround
            //ref: https://powerusers.microsoft.com/t5/Power-Apps-Ideas/Web-API-Implement-expand-on-collections/idi-p/221291

            IEnumerable<SSG_SearchApiRequest> searchApiRequests = await _oDataClient.For<SSG_SearchApiRequest>()
                .Select(x => x.SearchApiRequestId)
                .Filter(x => x.StatusCode == readyForSearchCode)
                .FindEntriesAsync(cancellationToken);

            foreach (SSG_SearchApiRequest request in searchApiRequests)
            {
                SSG_SearchApiRequest searchApiRequest = await _oDataClient.For<SSG_SearchApiRequest>()
                    .Key(request.SearchApiRequestId)
                    .Expand(x => x.Identifiers)
                    .Expand(x => x.DataProviders)
                    .Expand(x => x.SearchRequest)
                    .FindEntryAsync(cancellationToken);
                searchApiRequest.IsFailed = false;
                results.Add( searchApiRequest );
            }
            return results;       
        }

        /// <summary>
        /// Get the corresponding searchRequestId give a searchApiRequestId
        /// </summary>
        /// <param name="searchApiRequestId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Guid> GetLinkedSearchRequestIdAsync(Guid searchApiRequestId,
            CancellationToken cancellationToken)
        {
            if (searchApiRequestId == default || searchApiRequestId == Guid.Empty) throw new ArgumentNullException(nameof(searchApiRequestId));

            var result = await _oDataClient
                .For<SSG_SearchApiRequest>()
                .Key(searchApiRequestId)
                .Select(x => x.SearchRequestId)
                .FindEntryAsync(cancellationToken);

            return result.SearchRequestId;
        }

        /// <summary>
        /// Marks a search request in Progress
        /// </summary>
        /// <param name="searchApiRequestId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<SSG_SearchApiRequest> MarkInProgress(Guid searchApiRequestId, CancellationToken cancellationToken)
        {
            if (searchApiRequestId == default || searchApiRequestId == Guid.Empty) throw new ArgumentNullException(nameof(searchApiRequestId));

            return await _oDataClient
                .For<SSG_SearchApiRequest>()
                .Key(searchApiRequestId)
                .Set(new Entry { { Keys.DYNAMICS_STATUS_CODE_FIELD, SearchApiRequestStatusReason.InProgress.Value } })
                .UpdateEntryAsync(cancellationToken);
        }


        public async Task<SSG_SearchApiEvent> AddEventAsync(Guid searchApiRequestId, SSG_SearchApiEvent searchApiEvent,
            CancellationToken cancellationToken)
        {
            if (searchApiRequestId == default || searchApiRequestId == Guid.Empty) throw new ArgumentNullException(nameof(searchApiRequestId));

            searchApiEvent.SearchApiRequest = new SSG_SearchApiRequest() { SearchApiRequestId = searchApiRequestId};

            return await this._oDataClient.For<SSG_SearchApiEvent>().Set(searchApiEvent).InsertEntryAsync(cancellationToken);
        }

        public async Task<SSG_SearchApiRequest> MarkComplete(Guid searchApiRequestId, CancellationToken cancellationToken)
        {
            if (searchApiRequestId == default || searchApiRequestId == Guid.Empty) throw new ArgumentNullException(nameof(searchApiRequestId));

            return await _oDataClient
                .For<SSG_SearchApiRequest>()
                .Key(searchApiRequestId)
                .Set(new Entry { { Keys.DYNAMICS_STATUS_CODE_FIELD, SearchApiRequestStatusReason.Complete.Value } })
                .UpdateEntryAsync(cancellationToken);
        }

        /// <summary>
        /// Get all failed search request that has not exceeded the number of days to retry
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IEnumerable<SSG_SearchApiRequest>> GetAllValidFailedSearchRequest(CancellationToken cancellationToken)
        {
            IEnumerable<SSG_DataProvider> dataProviders = await GetDataProvidersList(cancellationToken);

            List<SSG_SearchApiRequest> results = new List<SSG_SearchApiRequest>();

            //todo: we need to change to use following code, but ODataClient 4 has problems with expand, curent implemented code is a workaround
            //ref: https://powerusers.microsoft.com/t5/Power-Apps-Ideas/Web-API-Implement-expand-on-collections/idi-p/221291

            foreach (SSG_DataProvider provider in dataProviders)
            {
                string adaptorName = provider.AdaptorName;
                int noOfDaysToRetry = provider.NumberOfDaysToRetry;

                IEnumerable<SSG_SearchapiRequestDataProvider> searchApiRequests = await _oDataClient.For<SSG_SearchapiRequestDataProvider>()
                    .Select(x => x.SearchAPIRequestId)
                    .Filter(x => x.NumberOfFailures > 0)
                    .Filter(x => x.AdaptorName == adaptorName)
                    .Filter(x => x.NumberOfFailures < noOfDaysToRetry)
                    .FindEntriesAsync(cancellationToken);

                foreach (SSG_SearchapiRequestDataProvider request in searchApiRequests)
                {

                    SSG_SearchApiRequest searchApiRequest = await _oDataClient.For<SSG_SearchApiRequest>()
                        .Key(request.SearchAPIRequestId)
                        .Expand(x => x.Identifiers)
                        .Expand(x => x.DataProviders)
                        .Expand(x => x.SearchRequest)
                        .FindEntryAsync(cancellationToken);
                    FilterAffectedDataProvider( provider, searchApiRequest);
                    searchApiRequest.IsFailed = true;
                    results.Add(searchApiRequest);



                }
            }
            return results;
        }

        public async Task<IEnumerable<SSG_DataProvider>> GetDataProvidersList(CancellationToken cancellationToken)
        {
            return await _oDataClient.For<SSG_DataProvider>()
                            .FindEntriesAsync(cancellationToken);
        }

        private static void FilterAffectedDataProvider(SSG_DataProvider provider, SSG_SearchApiRequest searchApiRequest)
        {
        
            List<SSG_SearchapiRequestDataProvider> ssgApiDataProviders = new List<SSG_SearchapiRequestDataProvider>();
            ssgApiDataProviders.AddRange(searchApiRequest.DataProviders);
            var list = ssgApiDataProviders.FindAll(x => x.AdaptorName == provider.AdaptorName);
            list.ForEach(x => x.TimeBetweenRetries = provider.TimeBetweenRetries);
            list.ForEach(x => x.NumberOfRetries = provider.NumberOfRetries);
            searchApiRequest.DataProviders = list.ToArray();
        }
    }
}