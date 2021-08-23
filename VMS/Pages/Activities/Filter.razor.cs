﻿using Blazored.Modal;
using Blazored.Modal.Services;
using Microsoft.AspNetCore.Components;
using System.Collections.Generic;
using System.Threading.Tasks;
using VMS.Application.Interfaces;
using VMS.Application.ViewModels;
using VMS.Domain.Models;

namespace VMS.Pages.Activities
{
    public partial class Filter : ComponentBase
    {
        private List<User> organizers;
        private FilterActivityViewModel filter;
        private List<AddressPath> provinces;
        private List<AddressPath> districts;

        [Parameter]
        public EventCallback<FilterActivityViewModel> FilterEventCallback { get; set; }

        [Inject]
        private IIdentityService IdentityService { get; set; }

        [Inject]
        private IModalService AreaModalService { get; set; }

        [Inject]
        private IModalService SkillModalService { get; set; }

        [Inject]
        private IAddressService AddressService { get; set; }

        protected async override Task OnInitializedAsync()
        {
            filter = new FilterActivityViewModel();
            organizers = IdentityService.GetAllOrganizers();
            provinces = await AddressService.GetAllProvincesAsync();
        }

        private async Task FilterAsync()
        {
            await FilterEventCallback.InvokeAsync(filter);
        }

        private void ShowAreasPopupAsync()
        {
            ModalParameters parameters = new();
            parameters.Add("SelectedAreas", filter.Areas);

            AreaModalService.Show<AreasPopup>("", parameters);
        }

        private void ShowSkillsPopupAsync()
        {
            ModalParameters parameters = new ModalParameters();
            parameters.Add("SelectedSkills", filter.Skills);

            SkillModalService.Show<SkillsPopup>("", parameters);
        }

        private async Task ProvinceSelectionChanged(int id)
        {
            filter.ProvinceId = id;
            filter.DistrictId = 0;
            districts = await AddressService.GetAllAddressPathsByParentIdAsync(filter.ProvinceId);
        }

        private void ClearFilter()
        {
            filter = new FilterActivityViewModel();
            districts = null;
        }
    }
}