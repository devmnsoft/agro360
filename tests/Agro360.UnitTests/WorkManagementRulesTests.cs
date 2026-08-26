using Agro360.Domain.Operations;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class WorkManagementRulesTests
{
 [Fact] public void Create_task_requires_title()=>Assert.Throws<DomainException>(()=>WorkManagementRules.ValidateTask("",Guid.NewGuid(),DateTimeOffset.UtcNow,"HIGH"));
 [Fact] public void Create_task_accepts_valid_data()=>WorkManagementRules.ValidateTask("Conferir estoque",Guid.NewGuid(),DateTimeOffset.UtcNow.AddDays(1),"CRITICAL");
 [Fact] public void Complete_task_requires_description()=>Assert.Throws<DomainException>(()=>WorkManagementRules.ValidateTaskTransition("COMPLETED",null));
 [Fact] public void Cancel_task_requires_reason()=>Assert.Throws<DomainException>(()=>WorkManagementRules.ValidateTaskTransition("CANCELLED"," "));
 [Fact] public void Cancel_task_accepts_reason()=>WorkManagementRules.ValidateTaskTransition("CANCELLED","Duplicada");
 [Fact] public void Reject_workflow_requires_reason()=>Assert.Throws<DomainException>(()=>WorkManagementRules.ValidateDecision("REJECTED",null));
 [Fact] public void Approve_workflow_is_valid()=>WorkManagementRules.ValidateDecision("APPROVED",null);
}
