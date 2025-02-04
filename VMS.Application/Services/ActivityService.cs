﻿using AutoMapper;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using VMS.Application.Interfaces;
using VMS.Application.ViewModels;
using VMS.Common.Enums;
using VMS.Domain.Interfaces;
using VMS.Domain.Models;
using VMS.GenericRepository;
using VMS.Infrastructure.Data.Context;
using VMS.Common;
using VMS.Common.Extensions;

namespace VMS.Application.Services
{
    public class ActivityService : BaseService, IActivityService
    {
        private readonly IGeoLocationService _addressLocationService;

        public ActivityService(IRepository repository,
                               IDbContextFactory<VmsDbContext> dbContextFactory,
                               IMapper mapper,
                               IGeoLocationService addressLocationService) : base(repository, dbContextFactory, mapper)
        {
            _addressLocationService = addressLocationService;
        }

        public async Task<PaginatedList<ActivityViewModel>> GetAllActivitiesAsync(Dictionary<ActOrderBy, bool> orderList = null, Coordinate userLocation = null, int pageSize = 8)
        {
            return await GetAllActivitiesAsync(new FilterActivityViewModel(), 1, orderList, userLocation, pageSize);
        }

        public async Task<PaginatedList<ActivityViewModel>> GetAllActivitiesAsync(FilterActivityViewModel filter, int currentPage, Dictionary<ActOrderBy, bool> orderList = null, Coordinate userLocation = null, int pageSize = 20)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            PaginationSpecification<Activity> specification = new()
            {
                Conditions = GetFilterActivityConditionsByFilter(filter),
                Includes = a => a.Include(x => x.Organizer)
                                 .Include(x => x.Recruitments)
                                 .ThenInclude(x => x.RecruitmentRatings),
                PageIndex = currentPage,
                PageSize = pageSize,
                OrderBy = GetOrderActivities(orderList, userLocation)
            };

            PaginatedList<Activity> activities = await _repository.GetListAsync(dbContext, specification);

            return _mapper.Map<PaginatedList<ActivityViewModel>>(activities);
        }
        public async Task<PaginatedList<ActivityViewModel>> GetAllActivitiesAsync(FilterActivityViewModel filter, int currentPage)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            PaginationSpecification<Activity> specification = new()
            {
                Conditions = GetFilterActivityConditionsByFilterAdmin(filter),
                Includes = a => a.Include(x => x.Organizer)
                                 .Include(x => x.Recruitments)
                                 .ThenInclude(x => x.RecruitmentRatings),
                OrderBy = GetOrderActivitiesByActType(),
                PageIndex = currentPage,
                PageSize = 20
            };

            PaginatedList<Activity> activities = await _repository.GetListAsync(dbContext, specification);

            PaginatedList<ActivityViewModel> paginatedList = _mapper.Map<PaginatedList<ActivityViewModel>>(activities);

            foreach (var item in paginatedList.Items)
            {
                item.Rating = GetRateOfActivity(item.Recruitments);
            }

            return paginatedList;
        }

        private static Func<IQueryable<Activity>, IOrderedQueryable<Activity>> GetOrderActivitiesByActType()
        {
            return x => x.OrderBy(x => x.IsApproved).ThenByDescending(x => x.CreatedDate);
        }

        private static List<Expression<Func<Activity, bool>>> GetFilterActivityConditionsByFilter(FilterActivityViewModel filter)
        {
            if (filter.IsSearch)
            {
                return new List<Expression<Func<Activity, bool>>>()
                {
                    a => !a.IsDeleted,
                    a => a.IsApproved,
                    a => a.Name.ToUpper().Trim().Contains(filter.SearchValue.ToUpper().Trim())
                };
            }
            else
            {
                return new List<Expression<Func<Activity, bool>>>()
                {
                    a => !a.IsDeleted,
                    a => a.IsApproved,
                    a => a.OrgId == filter.OrgId || string.IsNullOrEmpty(filter.OrgId),
                    a => filter.Areas.Select(x => x.Id).Any(z => z == a.AreaId) || filter.Areas.Count == 0,
                    a => a.IsVirtual == filter.Virtual || a.IsActual == filter.Actual || !filter.Virtual && !filter.Actual,
                    a => a.ActivityAddresses.Any(x => x.AddressPathId == filter.AddressPathId) || filter.AddressPathId == 0,
                    a => a.ActivitySkills.Select(activitySkills => activitySkills.SkillId)
                                         .Where(actSkillId => filter.Skills.Select(skill => skill.Id)
                                                                           .Any(skillId => skillId == actSkillId))
                                         .Count() == filter.Skills.Count,
                    GetFilterActByType(filter.ActType)
                };
            }
        }

        private static List<Expression<Func<Activity, bool>>> GetFilterActivityConditionsByFilterAdmin(FilterActivityViewModel filter)
        {
            if (filter.IsSearch)
            {
                return new List<Expression<Func<Activity, bool>>>()
                {
                    a => !a.IsDeleted,
                    a => a.Name.ToUpper().Trim().Contains(filter.SearchValue.ToUpper().Trim())
                };
            }
            else
            {
                int lastDay = DateTime.DaysInMonth(filter.DateTimeValue.Year, filter.DateTimeValue.Month);
                DateTime dateTime = new(filter.DateTimeValue.Year, filter.DateTimeValue.Month, lastDay);
                return new List<Expression<Func<Activity, bool>>>()
                {
                    a => !a.IsDeleted,
                    a => !filter.IsApproved.HasValue || a.IsApproved == filter.IsApproved.Value,
                    GetFilterActByType(filter.ActType),
                    a => a.OrgId == filter.OrgId || string.IsNullOrEmpty(filter.OrgId),
                    a => filter.Areas.Select(x => x.Id).Any(z => z == a.AreaId) || filter.Areas.Count == 0,
                    a => a.IsVirtual == filter.Virtual || a.IsActual == filter.Actual || !filter.Virtual && !filter.Actual,
                    a => a.ActivityAddresses.Any(x => x.AddressPathId == filter.AddressPathId) || filter.AddressPathId == 0,
                    a => a.ActivitySkills.Select(activitySkills => activitySkills.SkillId)
                                         .Where(actSkillId => filter.Skills.Select(skill => skill.Id)
                                                                           .Any(skillId => skillId == actSkillId))
                                         .Count() == filter.Skills.Count,
                    a => a.Organizer.Course == filter.Level || string.IsNullOrEmpty(filter.Level),
                    GetFilterActByMonth(filter.IsMonthFilter, dateTime)
                };
            }
        }

        private static Expression<Func<Activity, bool>> GetFilterActByMonth(bool isMonthFilter, DateTime dateTime)
        {

            if (!isMonthFilter)
            {
                return x => true;
            }
            else
            {
                return x => x.StartDate <= dateTime && x.EndDate >= dateTime;
            }
        }

        public async Task<List<ActivityViewModel>> GetFeaturedActivitiesAsync()
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Specification<Activity> specification = new()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>()
                {
                    a => a.IsPin
                },
                Includes = a => a.Include(x => x.Organizer)
            };

            List<Activity> activities = await _repository.GetListAsync(dbContext, specification);

            return _mapper.Map<List<ActivityViewModel>>(activities);
        }

        public async Task<int> AddActivityAsync(CreateActivityViewModel activityViewModel)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Activity activity = _mapper.Map<Activity>(activityViewModel);

            activity.CreatedDate = DateTime.Now;
            activity.CreatedBy = activity.OrgId;
            activity.IsApproved = false;

            Coordinate coordinateResponse = await _addressLocationService.GetCoordinateAsync(activityViewModel.FullAddress);
            activity.Location = _geometryFactory.CreatePoint(coordinateResponse);
            activity.Latitude = coordinateResponse.Y;
            activity.Longitude = coordinateResponse.X;
            activity.ActivitySkills = MapSkills(activityViewModel, activity);
            activity.ActivityAddresses = MapActivityAddresses(activityViewModel, activity);

            object[] id = await _repository.InsertAsync(dbContext, activity);

            return Convert.ToInt32(id[0]);
        }

        public async Task<CreateActivityViewModel> GetCreateActivityViewModelAsync(int activityId)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Activity activity = await _repository.GetAsync(dbContext, new Specification<Activity>()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>
                {
                    a => a.Id == activityId,
                    a => !a.IsDeleted
                },
                Includes = a => a.Include(x => x.ActivitySkills)
                                    .ThenInclude(s => s.Skill)
                                 .Include(x => x.ActivityAddresses)
                                    .ThenInclude(x => x.AddressPath)
                                 .Include(x => x.Area)
            });

            if (activity is null)
            {
                return null;
            }

            CreateActivityViewModel activityViewModel = _mapper.Map<CreateActivityViewModel>(activity);

            activityViewModel.AreaName = activity.Area.Name;
            activityViewModel.AreaIcon = activity.Area.Icon;
            activityViewModel.Skills = activity.ActivitySkills.Select(a => new SkillViewModel
            {
                Id = a.SkillId,
                Name = a.Skill.Name
            }).ToList();

            ActivityAddress province = activity.ActivityAddresses.FirstOrDefault(x => x.AddressPath.Depth == 1);
            if (province is not null)
            {
                activityViewModel.ProvinceId = province.AddressPath.Id;
                activityViewModel.Province = province.AddressPath.Name;
            }

            ActivityAddress district = activity.ActivityAddresses.FirstOrDefault(x => x.AddressPath.Depth == 2);
            if (district is not null)
            {
                activityViewModel.DistrictId = district.AddressPath.Id;
                activityViewModel.District = district.AddressPath.Name;
            }

            ActivityAddress ward = activity.ActivityAddresses.FirstOrDefault(x => x.AddressPath.Depth == 3);
            if (ward is not null)
            {
                activityViewModel.WardId = ward.AddressPath.Id;
                activityViewModel.Ward = ward.AddressPath.Name;
            }

            return activityViewModel;
        }

        public async Task UpdateActivityAsync(CreateActivityViewModel activityViewModel, int activityId)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Specification<Activity> specification = new()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>
                {
                    a => a.Id == activityId
                },
                Includes = a => a.Include(x => x.ActivitySkills)
                                    .ThenInclude(s => s.Skill)
                                 .Include(x => x.ActivityAddresses)
                                    .ThenInclude(s => s.AddressPath)
            };

            Activity activity = await _repository.GetAsync(dbContext, specification);

            activity = _mapper.Map(activityViewModel, activity);

            activity.IsApproved = false;
            activity.IsDay = false;
            activity.IsPoint = false;
            activity.NumberOfDays = 0;
            activity.IsSingleChoice = false;

            activity.UpdatedBy = activity.OrgId;
            activity.UpdatedDate = DateTime.Now;

            Coordinate coordinateResponse = await _addressLocationService.GetCoordinateAsync(activityViewModel.FullAddress);
            activity.Location = _geometryFactory.CreatePoint(coordinateResponse);
            activity.Latitude = coordinateResponse.Y;
            activity.Longitude = coordinateResponse.X;

            activity.ActivitySkills = MapSkills(activityViewModel, activity);
            activity.ActivityAddresses = MapActivityAddresses(activityViewModel, activity);

            activity.IsClosed = activity.CloseDate.Date < DateTime.Now.Date;

            await _repository.UpdateAsync(dbContext, activity);
        }

        public async Task DeleteActivityAsync(int activityId)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();
            Activity activity = await _repository.GetByIdAsync<Activity>(dbContext, activityId);
            await _repository.DeleteAsync(dbContext, activity);
        }

        public async Task<ViewActivityViewModel> GetViewActivityViewModelAsync(int activityId)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Specification<Activity> specification = new()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>
                {
                    a => a.Id == activityId,
                    a => !a.IsDeleted
                },
                Includes = a => a.Include(x => x.ActivitySkills).ThenInclude(s => s.Skill)
                                .Include(x => x.ActivityAddresses).ThenInclude(s => s.AddressPath)
                                .Include(x => x.Area)
                                .Include(x => x.Organizer)
            };

            Activity activity = await _repository.GetAsync(dbContext, specification);

            if (activity is null) return null;

            ViewActivityViewModel activityViewModel = _mapper.Map<ViewActivityViewModel>(activity);

            activityViewModel.Skills = activity.ActivitySkills.Select(a => new Skill
            {
                Id = a.SkillId,
                Name = a.Skill.Name,
                IsDeleted = a.Skill.IsDeleted
            }).ToList();

            activityViewModel.AddressPaths = activity.ActivityAddresses.Select(a => new AddressPath
            {
                Id = a.AddressPathId,
                Name = a.AddressPath.Name
            }).ToList();

            return activityViewModel;
        }

        public async Task<List<ViewActivityViewModel>> GetOtherActivitiesAsync(string orgId, int[] excludedActitivyIds)
        {
            if (string.IsNullOrEmpty(orgId))
            {
                return new List<ViewActivityViewModel>();
            }

            DbContext context = _dbContextFactory.CreateDbContext();

            List<Activity> activity = await _repository.GetListAsync(context, new Specification<Activity>()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>
                {
                    a => !excludedActitivyIds.Contains(a.Id),
                    a => a.OrgId == orgId,
                    a => !a.IsDeleted,
                    a => a.IsApproved
                },
            });

            return _mapper.Map<List<ViewActivityViewModel>>(activity);
        }

        private static ICollection<ActivitySkill> MapSkills(CreateActivityViewModel activityViewModel, Activity activity)
        {
            return activityViewModel.Skills.Select(s => new ActivitySkill
            {
                Activity = activity,
                SkillId = s.Id
            }).ToList();
        }

        private static ICollection<ActivityAddress> MapActivityAddresses(CreateActivityViewModel activityViewModel, Activity activity)
        {
            List<ActivityAddress> result = new()
            {
                new() { Activity = activity, AddressPathId = activityViewModel.ProvinceId },
                new() { Activity = activity, AddressPathId = activityViewModel.DistrictId }
            };

            if (activityViewModel.WardId > 0)
            {
                result.Add(new() { Activity = activity, AddressPathId = activityViewModel.WardId });
            }

            return result;
        }

        public async Task<List<ActivityViewModel>> GetOrgActsAsync(string id, StatusAct status)
        {
            DbContext context = _dbContextFactory.CreateDbContext();

            Specification<Activity> specification = new()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>
                {
                    a => a.OrgId == id,
                    a => !a.IsDeleted,
                    a => a.IsApproved,
                    GetFilterOrgActByDate(status == StatusAct.Ended, status == StatusAct.Current),
                    a => status != StatusAct.Favor || a.Favorites.Count > 0
                },
                Includes = activities => activities.Include(x => x.Favorites)
                                                    .Include(x => x.Recruitments)
                                                    .ThenInclude(x => x.RecruitmentRatings)
                                                    .Include(x => x.ActivityAddresses)
                                                    .ThenInclude(x => x.AddressPath),
                OrderBy = GetOrderByStatusAct(status),
                Take = GetTakeByStatusAct(status)
            };

            List<Activity> activity = await _repository.GetListAsync(context, specification);

            List<ActivityViewModel> activityViewModels = _mapper.Map<List<ActivityViewModel>>(activity);

            if (status == StatusAct.Ended)
            {
                foreach (var act in activityViewModels)
                {
                    act.Province = GetActProvince(act.ActivityAddresses);
                    act.Rating = GetRateOfActivity(act.Recruitments);
                }
            }

            return activityViewModels;
        }

        private static Func<IQueryable<Activity>, IOrderedQueryable<Activity>> GetOrderByStatusAct(StatusAct status)
        {
            if (status == StatusAct.Favor)
            {
                return x => x.OrderByDescending(x => x.Favorites.Count);
            }

            if (status == StatusAct.Ended)
            {
                return x => x.OrderByDescending(x => x.EndDate);
            }

            return x => x.OrderBy(x => x.IsClosed).ThenByDescending(x => x.Id);
        }

        private static int? GetTakeByStatusAct(StatusAct status)
        {
            return status == StatusAct.Ended ? 4 : status == StatusAct.Favor ? 8 : null;
        }

        private static string GetActProvince(ICollection<ActivityAddress> activityAddresses)
        {
            ActivityAddress address = activityAddresses.Where(a => a.AddressPath.Depth == 1).FirstOrDefault();
            if (address != null)
            {
                return address.AddressPath.Name;
            }
            else
            {
                return "Hồ Chí Minh";
            }
        }

        private static double GetRateOfActivity(ICollection<Recruitment> recruitments)
        {
            return recruitments.Sum(a => a.RecruitmentRatings.Where(x => !x.IsOrgRating).Sum(x => x.Rank))
                    / recruitments.Sum(a => a.RecruitmentRatings.Where(x => !x.IsOrgRating).Count());
        }

        private Func<IQueryable<Activity>, IOrderedQueryable<Activity>> GetOrderActivities(Dictionary<ActOrderBy, bool> orderList, Coordinate coordinate)
        {
            Point userLocation = _geometryFactory.CreatePoint(coordinate);

            if (orderList == null || userLocation == null)
            {
                return x => x.OrderByDescending(a => a.Id);
            }

            if (orderList[ActOrderBy.Newest] && orderList[ActOrderBy.Nearest] && orderList[ActOrderBy.Hottest])
            {
                return x => x.OrderByDescending(a => a.Id)
                            .ThenByDescending(a => a.Recruitments.Count)
                            .ThenBy(a => a.Location.Distance(userLocation));
            }

            if (orderList[ActOrderBy.Newest] && orderList[ActOrderBy.Hottest])
            {
                return x => x.OrderByDescending(a => a.Id)
                            .ThenByDescending(a => a.Recruitments.Count);
            }

            if (orderList[ActOrderBy.Newest] && orderList[ActOrderBy.Nearest])
            {
                return x => x.OrderByDescending(a => a.Id)
                            .ThenBy(a => a.Location.Distance(userLocation));
            }

            if (orderList[ActOrderBy.Hottest] && orderList[ActOrderBy.Nearest])
            {
                return x => x.OrderByDescending(a => a.Recruitments.Count)
                            .ThenBy(a => a.Location.Distance(userLocation));
            }

            if (orderList[ActOrderBy.Hottest])
            {
                return x => x.OrderByDescending(a => a.Recruitments.Count);
            }

            if (orderList[ActOrderBy.Nearest])
            {
                return x => x.OrderBy(a => a.Location.Distance(userLocation));
            }

            return x => x.OrderByDescending(a => a.Id);
        }

        public async Task CloseOrDeleteActivity(int activityId, bool isDelete = false, bool isClose = false)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Activity activity = await _repository.GetByIdAsync<Activity>(dbContext, activityId);

            activity.IsClosed = isClose;
            activity.IsDeleted = isDelete;

            await _repository.UpdateAsync(dbContext, activity);
        }

        public async Task<List<ActivityViewModel>> GetAllUserActivityViewModelsAsync(string userId, StatusAct statusAct, DateTime dateTime)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Specification<Activity> specification = new()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>()
                {
                    GetConditionByStatusAct(userId, statusAct, dateTime)
                },
                OrderBy = a => a.OrderByDescending(x => x.Id),
                Includes = a => a.Include(x => x.Organizer)
            };

            List<Activity> activities = await _repository.GetListAsync(dbContext, specification);

            var activityViewModels = _mapper.Map<List<ActivityViewModel>>(activities);

            activityViewModels.ForEach(a => a.Province = GetActProvince(a.ActivityAddresses));

            return activityViewModels;
        }

        private static Expression<Func<Activity, bool>> GetConditionByStatusAct(string userId, StatusAct statusAct, DateTime dateTime)
        {
            return statusAct switch
            {
                StatusAct.Favor => x => x.Favorites.Any(f => f.UserId == userId),
                StatusAct.Ended => x => x.EndDate < dateTime.Date && x.Recruitments.Any(x => x.UserId == userId),
                StatusAct.Current => x => (dateTime.Date <= x.CloseDate
                                       || (x.StartDate <= dateTime.Date && dateTime.Date <= x.EndDate))
                                       &&  x.Recruitments.Any(x => x.UserId == userId),
                _ => x => x.StartDate <= dateTime.Date && dateTime.Date <= x.EndDate && x.Recruitments.Any(x => x.UserId == userId),
            };
        }

        public async Task<PaginatedList<ActivityViewModel>> GetAllOrganizationActivityViewModelAsync(FilterOrgActivityViewModel filter, int currentPage)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            PaginationSpecification<Activity> specification = new()
            {
                Conditions = GetFilterOrgActByFilter(filter),
                Includes = a => a.Include(x => x.Organizer)
                                 .Include(x => x.Recruitments)
                                 .ThenInclude(x => x.RecruitmentRatings),
                OrderBy = a => a.OrderByDescending(x => x.Id),
                PageIndex = currentPage,
                PageSize = 20
            };

            PaginatedList<Activity> activities = await _repository.GetListAsync(dbContext, specification);

            var paginatedList = _mapper.Map<PaginatedList<ActivityViewModel>>(activities);

            paginatedList.Items.ForEach(a => a.Rating = GetRateOfActivity(a.Recruitments));

            return paginatedList;
        }

        private static List<Expression<Func<Activity, bool>>> GetFilterOrgActByFilter(FilterOrgActivityViewModel filter)
        {
            if (filter.IsSearch)
            {
                return new List<Expression<Func<Activity, bool>>>()
                {
                    a => !a.IsDeleted,
                    a => a.OrgId == filter.OrgId,
                    a => a.Name.ToUpper().Trim().Contains(filter.SearchValue.ToUpper().Trim())
                };
            }
            else
            {
                return new List<Expression<Func<Activity, bool>>>()
                {
                    a => !a.IsDeleted,
                    a => a.OrgId == filter.OrgId,
                    GetFilterOrgActByDate(filter.IsTookPlace, filter.IsHappenning),
                    a => a.IsVirtual == filter.IsVirtual || a.IsActual == filter.IsActual || !filter.IsVirtual && !filter.IsActual
                };
            }
        }

        private static Expression<Func<Activity, bool>> GetFilterActByType(StatusAct actType)
        {
            switch (actType)
            {
                case StatusAct.Upcoming:
                    return x => DateTime.Now.Date <= x.StartDate && x.StartDate <= DateTime.Now.Date.AddDays(15);
                case StatusAct.Happenning:
                    return x => (x.StartDate <= DateTime.Now.Date && DateTime.Now.Date <= x.EndDate) || x.CloseDate >= DateTime.Now.Date;
                case StatusAct.TookPlace:
                    return x => x.EndDate < DateTime.Now.Date;
                case StatusAct.Closed:
                    return x => x.CloseDate < DateTime.Now.Date || x.IsClosed;
                case StatusAct.Approved:
                    return x => x.IsApproved;
                case StatusAct.NotApproved:
                    return x => !x.IsApproved;
                default:
                    return x => true;
            }
        }
        private static Expression<Func<Activity, bool>> GetFilterOrgActByDate(bool isTookPlace, bool isHappenning)
        {
            if (isTookPlace && isHappenning)
            {
                return x => true;
            }

            if (isTookPlace)
            {
                return x => x.EndDate < DateTime.Now.Date;
            }

            if (isHappenning)
            {
                return x => (x.EndDate >= DateTime.Now.Date && x.StartDate <= DateTime.Now.Date)
                            || x.CloseDate >= DateTime.Now.Date;
            }

            return x => true;
        }

        public async Task CloseActivityDailyAsync()
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Specification<Activity> specification = new()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>()
                {
                    a => a.CloseDate < DateTime.Now.Date,
                    a => !a.IsClosed
                }
            };

            List<Activity> activities = await _repository.GetListAsync(dbContext, specification);

            activities.ForEach(a => a.IsClosed = true);

            await _repository.UpdateAsync<Activity>(dbContext, activities);
        }

        public async Task ChangePinStateListActivityAsync(List<ActivityViewModel> activityViewModels)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Specification<Activity> specification = new()
            {
                Conditions = new List<Expression<Func<Activity, bool>>>()
                {
                    a => activityViewModels.Select(x => x.Id).Any(x => x == a.Id)
                }
            };

            List<Activity> activities = await _repository.GetListAsync(dbContext, specification);

            activities.ForEach(a => a.IsPin = activityViewModels.Find(x => x.Id == a.Id).IsPin);

            await _repository.UpdateAsync<Activity>(dbContext, activities);
        }

        public async Task ApproveActAsync(int id, bool isPoint, bool isDay, int numberOfDay, bool isSingleChoice)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Activity activity = await _repository.GetByIdAsync<Activity>(dbContext, id);

            activity.IsApproved = true;
            activity.IsPoint = isPoint;
            activity.IsDay = isDay;
            activity.NumberOfDays = numberOfDay;
            activity.IsSingleChoice = isSingleChoice;

            await _repository.UpdateAsync(dbContext, activity);
        }

        public async Task EditRequirementActAsync(EditRequirementViewModel editRequirementViewModel)
        {
            DbContext dbContext = _dbContextFactory.CreateDbContext();

            Feedback report = _mapper.Map<Feedback>(editRequirementViewModel);

            report.ImageReports = editRequirementViewModel.Images.Select(x => new ImageReport()
            {
                Image = x,
                Feedback = report
            }).ToList();

            await _repository.InsertAsync(dbContext, report);
        }
    }
}