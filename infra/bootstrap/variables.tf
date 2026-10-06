variable "location" {
  type        = string
  description = "Azure region of the state storage, identities and resource groups (EEA, C27)."
}

variable "github_repo" {
  type        = string
  description = "GitHub repository as owner/name; the federated credential subjects are built from it (C52)."
}

variable "owner_object_id" {
  type        = string
  description = "Entra object ID of the owner, who applies this root."
}

variable "admin_object_id" {
  type        = string
  description = "Entra object ID of the admin's account, which runs the C25 secret script."
}

variable "admin_email" {
  type        = string
  sensitive   = true
  description = "C66 admin address for the budget alert. Supplied as TF_VAR_admin_email, never committed."
}

variable "budget_extra_emails" {
  type        = list(string)
  sensitive   = true
  description = "Further budget alert recipients. Supplied as TF_VAR_budget_extra_emails, never committed."
}
