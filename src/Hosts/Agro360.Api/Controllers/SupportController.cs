using System.Globalization;
using System.Text;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/support"),Authorize(Policy=Permissions.SupportRead)]
public sealed class SupportController(ISupportCustomerSuccessService service,ILogger<SupportController> logger):ControllerBase
{
 [HttpGet("dashboard")] public Task<SupportDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 [HttpGet("lookups/tenants"),Authorize(Policy=Permissions.SupportManage)] public Task<IReadOnlyList<SupportLookup>> Tenants(string? search,CancellationToken ct)=>service.TenantsAsync(search,ct);
 [HttpGet("lookups/agents")] public Task<IReadOnlyList<SupportLookup>> Agents(string? search,CancellationToken ct)=>service.AgentsAsync(search,ct);
 [HttpGet("tickets")] public Task<IReadOnlyList<SupportTicket>> Tickets(string? search,string? status,string? priority,int page=1,int pageSize=25,CancellationToken ct=default)=>service.TicketsAsync(search,status,priority,page,pageSize,ct);
 [HttpGet("tickets/{id:guid}")] public async Task<IActionResult> Ticket(Guid id,CancellationToken ct)=>await service.TicketAsync(id,ct) is { } x?Ok(x):NotFound();
 [HttpPost("tickets"),Authorize(Policy=Permissions.SupportWrite)] public Task<IActionResult> Ticket(TicketCommand x,CancellationToken ct)=>Created("tickets",()=>service.CreateTicketAsync(x,ct));
 [HttpPost("tickets/{id:guid}/transition"),Authorize(Policy=Permissions.SupportWrite)] public Task<IActionResult> Transition(Guid id,TicketTransition x,CancellationToken ct)=>NoContent("ticket-transition",()=>service.TransitionTicketAsync(id,x,ct));
 [HttpPost("tickets/{id:guid}/comments"),Authorize(Policy=Permissions.SupportWrite)] public Task<IActionResult> Comment(Guid id,TicketCommentCommand x,CancellationToken ct)=>NoContent("ticket-comment",()=>service.CommentAsync(id,x,ct));
 [HttpGet("tickets/{id:guid}/events")] public Task<IReadOnlyList<SupportEvent>> Events(Guid id,CancellationToken ct)=>service.EventsAsync(id,ct);
 [HttpGet("tickets.csv")] public async Task<IActionResult> Csv(CancellationToken ct){var rows=await service.TicketsAsync(null,null,null,1,10000,ct);var csv=new StringBuilder("codigo;titulo;categoria;prioridade;severidade;status;abertura;prazo_resolucao\n");foreach(var x in rows)csv.AppendLine(CultureInfo.InvariantCulture,$"{Cell(x.PublicCode)};{Cell(x.Title)};{Cell(x.Category)};{x.Priority};{x.Severity};{x.Status};{x.OpenedAt:O};{x.ResolutionDueAt:O}");return File(Encoding.UTF8.GetBytes(csv.ToString()),"text/csv; charset=utf-8","chamados.csv");}
 [HttpGet("sla")] public Task<IReadOnlyList<SlaPolicy>> Sla(CancellationToken ct)=>service.SlaAsync(ct);
 [HttpPost("sla"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Sla(SlaPolicyCommand x,CancellationToken ct)=>Created("sla",()=>service.SaveSlaAsync(x,ct));
 [HttpGet("articles")] public Task<IReadOnlyList<KnowledgeArticle>> Articles(string? search,[FromQuery(Name="module")] string? moduleCode,bool publicOnly=true,CancellationToken ct=default)=>service.ArticlesAsync(search,moduleCode,publicOnly,ct);
 [HttpPost("articles"),Authorize(Policy=Permissions.SupportWrite)] public Task<IActionResult> Article(ArticleCommand x,CancellationToken ct)=>Created("articles",()=>service.CreateArticleAsync(x,ct));
 [HttpPost("articles/{id:guid}/publish"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Publish(Guid id,CancellationToken ct)=>NoContent("article-publish",()=>service.PublishArticleAsync(id,false,ct));
 [HttpPost("articles/{id:guid}/archive"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Archive(Guid id,CancellationToken ct)=>NoContent("article-archive",()=>service.PublishArticleAsync(id,true,ct));
 [HttpPost("articles/{id:guid}/feedback")] public Task<IActionResult> ArticleFeedback(Guid id,ArticleFeedbackCommand x,CancellationToken ct)=>NoContent("article-feedback",()=>service.RateArticleAsync(id,x,ct));
 [HttpGet("implementations")] public Task<IReadOnlyList<ImplementationProject>> Implementations(CancellationToken ct)=>service.ImplementationsAsync(ct);
 [HttpPost("implementations"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Implementation(ImplementationCommand x,CancellationToken ct)=>Created("implementations",()=>service.CreateImplementationAsync(x,ct));
 [HttpPost("implementation-phases/{id:guid}/complete"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Phase(Guid id,PhaseCompletionCommand x,CancellationToken ct)=>NoContent("phase-complete",()=>service.CompletePhaseAsync(id,x,ct));
 [HttpGet("trainings")] public Task<IReadOnlyList<TrainingTrack>> Trainings(CancellationToken ct)=>service.TrainingsAsync(ct);
 [HttpPost("trainings"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Training(TrainingCommand x,CancellationToken ct)=>Created("trainings",()=>service.CreateTrainingAsync(x,ct));
 [HttpPost("training-assignments/{id:guid}/complete")] public Task<IActionResult> CompleteTraining(Guid id,TrainingCompletionCommand x,CancellationToken ct)=>NoContent("training-complete",()=>service.CompleteTrainingAsync(id,x,ct));
 [HttpGet("feedback")] public Task<IReadOnlyList<CustomerFeedback>> Feedback(CancellationToken ct)=>service.FeedbackAsync(ct);
 [HttpPost("feedback")] public Task<IActionResult> Feedback(FeedbackCommand x,CancellationToken ct)=>Created("feedback",()=>service.CreateFeedbackAsync(x,ct));
 [HttpPost("feedback/{id:guid}/convert"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Convert(Guid id,CancellationToken ct)=>Created("backlog",()=>service.ConvertFeedbackAsync(id,ct));
 [HttpGet("backlog"),Authorize(Policy=Permissions.SupportManage)] public Task<IReadOnlyList<BacklogItem>> Backlog(CancellationToken ct)=>service.BacklogAsync(ct);
 [HttpPost("backlog"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Backlog(BacklogCommand x,CancellationToken ct)=>Created("backlog",()=>service.CreateBacklogAsync(x,ct));
 [HttpGet("releases")] public Task<IReadOnlyList<ReleaseNote>> Releases(CancellationToken ct)=>service.ReleasesAsync(ct);
 [HttpPost("releases"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> Release(ReleaseNoteCommand x,CancellationToken ct)=>Created("releases",()=>service.CreateReleaseAsync(x,ct));
 [HttpPost("releases/{id:guid}/publish"),Authorize(Policy=Permissions.SupportManage)] public Task<IActionResult> PublishRelease(Guid id,CancellationToken ct)=>NoContent("release-publish",()=>service.PublishReleaseAsync(id,ct));
 [HttpPost("releases/{id:guid}/read")] public Task<IActionResult> ReadRelease(Guid id,CancellationToken ct)=>NoContent("release-read",()=>service.ReadReleaseAsync(id,ct));
 private async Task<IActionResult> Created(string route,Func<Task<Guid>> action){try{var id=await action();return Created($"/api/support/{route}/{id}",new{id});}catch(Exception ex){ApiLogMessages.SupportOperationFailed(logger,route,ex);throw;}}
 private async Task<IActionResult> NoContent(string operation,Func<Task> action){try{await action();return NoContent();}catch(Exception ex){ApiLogMessages.SupportOperationFailed(logger,operation,ex);throw;}}
 private static string Cell(string value)=>$"\"{value.Replace("\"","\"\"")}\"";
}
