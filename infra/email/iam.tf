# Sending keys are created by the owner in the console and written to Key Vault by
# tools/set-secret.sh; an API key resource here would put the secret in state (C50).
resource "scaleway_iam_application" "sender" {
  for_each = var.environments

  name        = "climbon-${each.key}-sender"
  description = "climb-on ${each.key}: sends email over SMTP in its own project only"
}

resource "scaleway_iam_policy" "sender" {
  for_each = var.environments

  name           = "climbon-${each.key}-sender"
  description    = "SMTP sending in climbon-${each.key} only"
  application_id = scaleway_iam_application.sender[each.key].id

  rule {
    project_ids          = [scaleway_account_project.env[each.key].id]
    permission_set_names = ["TransactionalEmailEmailSmtpCreate"]
  }
}
