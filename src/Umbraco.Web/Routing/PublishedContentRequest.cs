﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Routing;
using Microsoft.Extensions.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Web.Models;

namespace Umbraco.Web.Routing
{
    /// <summary>
    /// Represents a request for one specified Umbraco IPublishedContent to be rendered
    /// by one specified template, using one specified Culture and RenderingEngine.
    /// </summary>
    public class PublishedContentRequest
    {
        private readonly ITemplateService _templateService;
        private bool _readonly;

        /// <summary>
        /// Triggers once the published content request has been prepared, but before it is processed.
        /// </summary>
        /// <remarks>When the event triggers, preparation is done ie domain, culture, document, template,
        /// rendering engine, etc. have been setup. It is then possible to change anything, before
        /// the request is actually processed and rendered by Umbraco.</remarks>
        public static event EventHandler<EventArgs> Prepared;

        // the engine that does all the processing
        // because in order to keep things clean and separated,
        // the content request is just a data holder
        private readonly PublishedContentRequestEngine _engine;

        /// <summary>
        /// Initializes a new instance of the <see cref="PublishedContentRequest"/> class with a specific Uri and routing context.
        /// </summary>
        /// <param name="routingContext">A routing context.</param>
        /// <param name="templateService"></param>
        /// <param name="loggerFactor"></param>
        /// <param name="httpContextAccessor"></param>
        public PublishedContentRequest(RoutingContext routingContext, ITemplateService templateService, ILoggerFactory loggerFactor, IHttpContextAccessor httpContextAccessor)
        {            
            if (routingContext == null) throw new ArgumentNullException("routingContext");
            if (templateService == null) throw new ArgumentNullException(nameof(templateService));
            if (loggerFactor == null) throw new ArgumentNullException(nameof(loggerFactor));
            if (httpContextAccessor == null) throw new ArgumentNullException(nameof(httpContextAccessor));

            _templateService = templateService; 
            RoutingContext = routingContext;

            _engine = new PublishedContentRequestEngine(templateService, this, loggerFactor, httpContextAccessor);
        }

        /// <summary>
        /// Gets the engine associated to the request.
        /// </summary>
        internal PublishedContentRequestEngine Engine => _engine;

        /// <summary>
        /// Prepares the request.
        /// </summary>
        public async Task<bool> PrepareAsync(RouteData routeData)
        {
            RouteData = routeData;
            var result = await _engine.PrepareRequestAsync();
            return result;
        }

        public bool IsPrepared => RouteData != null;

        /// <summary>
        /// Updates the request when there is no template to render the content.
        /// </summary>
        internal async Task UpdateOnMissingTemplateAsync()
        {
            var __readonly = _readonly;
            _readonly = false;
            await _engine.UpdateRequestOnMissingTemplateAsync();
            _readonly = __readonly;
        }

        /// <summary>
        /// Triggers the Prepared event.
        /// </summary>
        internal void OnPrepared()
        {
            if (Prepared != null)
                Prepared(this, EventArgs.Empty);

            if (HasPublishedContent == false)
                Is404 = true; // safety

            _readonly = true;
        }

        ///// <summary>
        ///// Gets or sets the cleaned up Uri used for routing.
        ///// </summary>
        ///// <remarks>The cleaned up Uri has no virtual directory, no trailing slash, no .aspx extension, etc.</remarks>
        //public Uri Uri { get; private set; }

        private void EnsureWriteable()
        {
            if (_readonly)
                throw new InvalidOperationException("Cannot modify a PublishedContentRequest once it is read-only.");
        }

        #region PublishedContent

        /// <summary>
        /// The requested IPublishedContent, if any, else <c>null</c>.
        /// </summary>
        private IPublishedContent _publishedContent;

        public RouteData RouteData { get; private set; }

        /// <summary>
        /// Gets or sets the requested content.
        /// </summary>
        /// <remarks>Setting the requested content clears <c>Template</c>.</remarks>
        public IPublishedContent PublishedContent
        {
            get { return _publishedContent; }
            set
            {
                EnsureWriteable();
                _publishedContent = value;
                IsInternalRedirectPublishedContent = false;
                TemplateModel = null;
            }
        }

        /// <summary>
        /// Sets the requested content, following an internal redirect.
        /// </summary>
        /// <param name="content">The requested content.</param>
        /// <remarks>Depending on <c>UmbracoSettings.InternalRedirectPreservesTemplate</c>, will
        /// preserve or reset the template, if any.</remarks>
        public void SetInternalRedirectPublishedContent(IPublishedContent content)
        {
            EnsureWriteable();

            // unless a template has been set already by the finder,
            // template should be null at that point. 

            // IsInternalRedirect if IsInitial, or already IsInternalRedirect
            var isInternalRedirect = IsInitialPublishedContent || IsInternalRedirectPublishedContent;

            // redirecting to self
            if (content.Id == PublishedContent.Id) // neither can be null
            {
                // no need to set PublishedContent, we're done
                IsInternalRedirectPublishedContent = isInternalRedirect;
                return;
            }

            // else

            // save
            var template = TemplateModel;

            // set published content - this resets the template, and sets IsInternalRedirect to false
            PublishedContent = content;
            IsInternalRedirectPublishedContent = isInternalRedirect;

            // must restore the template if it's an internal redirect & the config option is set
            if (isInternalRedirect)
            {
                // restore
                TemplateModel = template;
            }
        }

        /// <summary>
		/// Gets the initial requested content.
		/// </summary>
		/// <remarks>The initial requested content is the content that was found by the finders,
		/// before anything such as 404, redirect... took place.</remarks>
		public IPublishedContent InitialPublishedContent { get; private set; }

        /// <summary>
        /// Gets value indicating whether the current published content is the initial one.
        /// </summary>
        public bool IsInitialPublishedContent => InitialPublishedContent != null && InitialPublishedContent == _publishedContent;

        /// <summary>
        /// Indicates that the current PublishedContent is the initial one.
        /// </summary>
        public void SetIsInitialPublishedContent()
        {
            EnsureWriteable();

            // note: it can very well be null if the initial content was not found
            InitialPublishedContent = _publishedContent;
            IsInternalRedirectPublishedContent = false;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the current published content has been obtained
        /// from the initial published content following internal redirections exclusively.
        /// </summary>
        /// <remarks>Used by PublishedContentRequestEngine.FindTemplate() to figure out whether to
        /// apply the internal redirect or not, when content is not the initial content.</remarks>
        public bool IsInternalRedirectPublishedContent { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the content request has a content.
        /// </summary>
        public bool HasPublishedContent => PublishedContent != null;

        #endregion

        #region Template

        /// <summary>
        /// Gets or sets the template model to use to display the requested content.
        /// </summary>
        internal ITemplate TemplateModel { get; private set; }

        /// <summary>
        /// Gets the alias of the template to use to display the requested content.
        /// </summary>
        public string TemplateAlias => TemplateModel?.Alias;

        /// <summary>
        /// Tries to set the template to use to display the requested content.
        /// </summary>
        /// <param name="alias">The alias of the template.</param>
        /// <returns>A value indicating whether a valid template with the specified alias was found.</returns>
        /// <remarks>
        /// <para>Successfully setting the template does refresh <c>RenderingEngine</c>.</para>
        /// <para>If setting the template fails, then the previous template (if any) remains in place.</para>
        /// </remarks>
        public bool TrySetTemplate(string alias)
        {
            EnsureWriteable();

            if (string.IsNullOrWhiteSpace(alias))
            {
                TemplateModel = null;
                return true;
            }

            // NOTE - can we stil get it with whitespaces in it due to old legacy bugs?
            alias = alias.Replace(" ", "");

            var model = _templateService.GetTemplate(alias);
            if (model == null)
                return false;

            TemplateModel = model;
            return true;
        }

        /// <summary>
        /// Sets the template to use to display the requested content.
        /// </summary>
        /// <param name="template">The template.</param>
        /// <remarks>Setting the template does refresh <c>RenderingEngine</c>.</remarks>
        public void SetTemplate(ITemplate template)
        {
            EnsureWriteable();
            TemplateModel = template;
        }

        /// <summary>
        /// Resets the template.
        /// </summary>
        /// <remarks>The <c>RenderingEngine</c> becomes unknown.</remarks>
	    public void ResetTemplate()
        {
            EnsureWriteable();
            TemplateModel = null;
        }

        /// <summary>
        /// Gets a value indicating whether the content request has a template.
        /// </summary>
        public bool HasTemplate => TemplateModel != null;

        #endregion

        #region Domain and Culture

        //TODO: Should we publicize the setter now that we are using a non-legacy entity??
        /// <summary>
        /// Gets or sets the content request's domain.
        /// </summary>
        public IDomain UmbracoDomain { get; internal set; }

        /// <summary>
        /// Gets or sets the content request's domain Uri.
        /// </summary>
        /// <remarks>The <c>Domain</c> may contain "example.com" whereas the <c>Uri</c> will be fully qualified eg "http://example.com/".</remarks>
        public Uri DomainUri { get; internal set; }

        /// <summary>
        /// Gets a value indicating whether the content request has a domain.
        /// </summary>
        public bool HasDomain
        {
            get { return UmbracoDomain != null; }
        }

        private CultureInfo _culture;

        /// <summary>
        /// Gets or sets the content request's culture.
        /// </summary>
        public CultureInfo Culture
        {
            get { return _culture; }
            set
            {
                EnsureWriteable();
                _culture = value;
            }
        }

        // note: do we want to have an ordered list of alternate cultures,
        // to allow for fallbacks when doing dictionnary lookup and such?

        #endregion

        /// <summary>
        /// Gets or sets the current RoutingContext.
        /// </summary>
        public RoutingContext RoutingContext { get; private set; }
        
      
        #region Status

        /// <summary>
        /// Gets or sets a value indicating whether the requested content could not be found.
        /// </summary>
        /// <remarks>This is set in the <c>PublishedContentRequestBuilder</c>.</remarks>
        public bool Is404 { get; internal set; }

        /// <summary>
        /// Indicates that the requested content could not be found.
        /// </summary>
        /// <remarks>This is for public access, in custom content finders or <c>Prepared</c> event handlers,
        /// where we want to allow developers to indicate a request is 404 but not to cancel it.</remarks>
        public void SetIs404()
        {
            EnsureWriteable();
            Is404 = true;
        }

        /// <summary>
        /// Gets a value indicating whether the content request triggers a redirect (permanent or not).
        /// </summary>
        public bool IsRedirect { get { return string.IsNullOrWhiteSpace(RedirectUrl) == false; } }

        /// <summary>
        /// Gets or sets a value indicating whether the redirect is permanent.
        /// </summary>
        public bool IsRedirectPermanent { get; private set; }

        /// <summary>
        /// Gets or sets the url to redirect to, when the content request triggers a redirect.
        /// </summary>
        public string RedirectUrl { get; private set; }

        /// <summary>
        /// Indicates that the content request should trigger a redirect (302).
        /// </summary>
        /// <param name="url">The url to redirect to.</param>
        /// <remarks>Does not actually perform a redirect, only registers that the response should
        /// redirect. Redirect will or will not take place in due time.</remarks>
        public void SetRedirect(string url)
        {
            EnsureWriteable();
            RedirectUrl = url;
            IsRedirectPermanent = false;
        }

        /// <summary>
        /// Indicates that the content request should trigger a permanent redirect (301).
        /// </summary>
        /// <param name="url">The url to redirect to.</param>
        /// <remarks>Does not actually perform a redirect, only registers that the response should
        /// redirect. Redirect will or will not take place in due time.</remarks>
        public void SetRedirectPermanent(string url)
        {
            EnsureWriteable();
            RedirectUrl = url;
            IsRedirectPermanent = true;
        }

        /// <summary>
        /// Indicates that the content requet should trigger a redirect, with a specified status code.
        /// </summary>
        /// <param name="url">The url to redirect to.</param>
        /// <param name="status">The status code (300-308).</param>
        /// <remarks>Does not actually perform a redirect, only registers that the response should
        /// redirect. Redirect will or will not take place in due time.</remarks>
        public void SetRedirect(string url, int status)
        {
            EnsureWriteable();

            if (status < 300 || status > 308)
                throw new ArgumentOutOfRangeException("status", "Valid redirection status codes 300-308.");

            RedirectUrl = url;
            IsRedirectPermanent = (status == 301 || status == 308);
            if (status != 301 && status != 302) // default redirect statuses
                ResponseStatusCode = status;
        }

        /// <summary>
        /// Gets or sets the content request http response status code.
        /// </summary>
        /// <remarks>Does not actually set the http response status code, only registers that the response
        /// should use the specified code. The code will or will not be used, in due time.</remarks>
        public int ResponseStatusCode { get; private set; }

        /// <summary>
        /// Gets or sets the content request http response status description.
        /// </summary>
        /// <remarks>Does not actually set the http response status description, only registers that the response
        /// should use the specified description. The description will or will not be used, in due time.</remarks>
        public string ResponseStatusDescription { get; private set; }

        /// <summary>
        /// Sets the http response status code, along with an optional associated description.
        /// </summary>
        /// <param name="code">The http status code.</param>
        /// <param name="description">The description.</param>
        /// <remarks>Does not actually set the http response status code and description, only registers that
        /// the response should use the specified code and description. The code and description will or will
        /// not be used, in due time.</remarks>
        public void SetResponseStatus(int code, string description = null)
        {
            EnsureWriteable();

            // .Status is deprecated
            // .SubStatusCode is IIS 7+ internal, ignore
            ResponseStatusCode = code;
            ResponseStatusDescription = description;
        }

        #endregion
    }
}
