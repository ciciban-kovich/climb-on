resource "scaleway_tem_domain" "env" {
  for_each = var.environments

  project_id = scaleway_account_project.env[each.key].id
  name       = "${each.value}.${var.domain}"
  region     = "fr-par"
  accept_tos = true
}

# Waits until TEM has found the DNS records and marked the domain verified.
resource "scaleway_tem_domain_validation" "env" {
  for_each = var.environments

  domain_id = scaleway_tem_domain.env[each.key].id
  region    = scaleway_tem_domain.env[each.key].region
  timeout   = 3600

  depends_on = [
    scaleway_domain_record.spf,
    scaleway_domain_record.dkim,
    scaleway_domain_record.mx,
    scaleway_domain_record.dmarc,
  ]
}
