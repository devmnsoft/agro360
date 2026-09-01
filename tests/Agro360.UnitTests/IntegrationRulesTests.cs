using System.ComponentModel.DataAnnotations;
using Agro360.Application.Contracts;

namespace Agro360.UnitTests;

public sealed class IntegrationRulesTests
{
 [Fact] public void Api_key_requires_scope()=>Assert.Contains(Validate(new ApiKeyCommand("mobile",[],null)),x=>x.MemberNames.Contains("Scopes"));
 [Fact] public void Api_key_accepts_external_scope()=>Assert.Empty(Validate(new ApiKeyCommand("partner",["properties.read"],DateTimeOffset.UtcNow.AddDays(30),60)));
 [Fact] public void Integration_requires_provider()=>Assert.NotEmpty(Validate(new IntegrationCommand("ERP","FISCAL","",null)));
 [Fact] public void Webhook_requires_valid_url()=>Assert.NotEmpty(Validate(new WebhookCommand("not-a-url",["LOT_CREATED"],Guid.NewGuid())));
 [Fact] public void Import_rejects_unknown_entity()=>Assert.NotEmpty(Validate(new ImportCommand("UNKNOWN","items.csv","name\nMilho")));
 [Fact] public void Fiscal_document_rejects_unsupported_type()=>Assert.NotEmpty(Validate(new IntegrationFiscalDocumentCommand("INVOICE","x.xml","eA==",null,null,null,null,null,null)));
 [Fact] public void Reading_requires_device_token()=>Assert.NotEmpty(Validate(new ReadingCommand("","TEMPERATURE",3,"C",null,null,null)));
 [Fact] public void Split_requires_participants()=>Assert.NotEmpty(Validate(new SplitCommand(Guid.NewGuid(),100,[])));
 [Fact] public void Message_rejects_unknown_channel()=>Assert.NotEmpty(Validate(new MessageCommand("PUSH",Guid.NewGuid(),"Alerta","Texto")));
 private static List<ValidationResult> Validate(object value){var result=new List<ValidationResult>();Validator.TryValidateObject(value,new ValidationContext(value),result,true);return result;}
}
