using Agro360.Domain.Operations;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class WorkflowAutomationRulesTests
{
    [Fact] public void Workflow_without_steps_cannot_activate()=>Assert.Throws<DomainException>(()=>WorkflowAutomationRules.ValidateActivation(0));
    [Fact] public void Active_workflow_requires_new_version()=>Assert.Throws<DomainException>(()=>WorkflowAutomationRules.ValidateDefinitionChange(true));
    [Fact] public void Approval_step_requires_assignee()=>Assert.Throws<DomainException>(()=>WorkflowAutomationRules.ValidateStep("APPROVAL",1,null,null,false,null,false,null));
    [Fact] public void Required_evidence_blocks_completion()=>Assert.Throws<DomainException>(()=>WorkflowAutomationRules.ValidateStep("EVIDENCE",1,null,null,true,null,false,null));
    [Fact] public void Complete_step_accepts_required_inputs()=>WorkflowAutomationRules.ValidateStep("EVIDENCE",1,null,null,true,"storage/key",true,"Conferido");
    [Fact] public void Active_automation_requires_condition()=>Assert.Throws<DomainException>(()=>WorkflowAutomationRules.ValidateAutomation("TASK_COMPLETED","CREATE_ALERT","{}",true));
    [Fact] public void Automation_rejects_arbitrary_sql()=>Assert.Throws<DomainException>(()=>WorkflowAutomationRules.ValidateAutomation("TASK_COMPLETED","CREATE_ALERT","{\"x\":\"drop table users\"}",false));
    [Fact] public void Automation_accepts_valid_condition()=>WorkflowAutomationRules.ValidateAutomation("TASK_COMPLETED","CREATE_ALERT","{\"status\":\"COMPLETED\"}",true);
    [Fact] public void Template_rejects_unknown_variable()=>Assert.Throws<DomainException>(()=>WorkflowAutomationRules.ValidateTemplate("EMAIL","Prazo","Olá {{secret}}",["user.name"]));
    [Fact] public void Template_rejects_unsafe_html()=>Assert.Throws<DomainException>(()=>WorkflowAutomationRules.ValidateTemplate("EMAIL","Prazo","<script>alert(1)</script>",[]));
}
