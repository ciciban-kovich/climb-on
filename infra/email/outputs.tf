# Per environment, the email_* values go into infra/env/<env>.tfvars (task 30); verify.sh
# compares them.
output "email" {
  description = "SMTP settings and the sender application, per environment."
  value = {
    for env, sub in var.environments : env => {
      email_smtp_host       = scaleway_tem_domain.env[env].smtp_host
      email_smtp_port       = 587
      email_smtp_username   = scaleway_account_project.env[env].id
      email_sender_address  = "noreply@${sub}.${var.domain}"
      sender_application_id = scaleway_iam_application.sender[env].id
    }
  }
}
