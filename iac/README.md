# Azure Form Recognizer and Translator Infrastructure as Code

This directory contains Terraform code to provision Azure resources for the RKW Journal Reader project, including Form Recognizer, Translator, Key Vault, Storage, and supporting infrastructure.

## Resources Provisioned

| Resource Type                      | Terraform Name                        | Purpose                                 |
|------------------------------------|---------------------------------------|-----------------------------------------|
| azurerm_resource_group             | azurerm_resource_group.rkw            | Resource group for all resources        |
| azurerm_storage_account            | azurerm_storage_account.rkw           | Storage for images and outputs          |
| azurerm_key_vault                  | azurerm_key_vault.rkw                 | Secure storage for secrets              |
| azurerm_cognitive_account          | azurerm_cognitive_account.rkw-fr      | Azure Form Recognizer resource          |
| azurerm_key_vault_secret           | azurerm_key_vault_secret.api_key_1_fr | Primary API key for Form Recognizer     |
| azurerm_key_vault_secret           | azurerm_key_vault_secret.api_key_2_fr | Secondary API key for Form Recognizer   |
| azurerm_key_vault_secret           | azurerm_key_vault_secret.endpoint_fr  | Endpoint URL for Form Recognizer        |
| azurerm_cognitive_account          | azurerm_cognitive_account.translator  | Azure Translator resource               |
| azurerm_key_vault_secret           | azurerm_key_vault_secret.translator_key | API key for Translator                |
| azurerm_key_vault_secret           | azurerm_key_vault_secret.translator_endpoint | Endpoint URL for Translator      |
| azurerm_key_vault_secret           | azurerm_key_vault_secret.translator_region | Region for Translator              |

## Usage

1. **Prerequisites**
   - Azure subscription
   - Terraform installed
   - Sufficient permissions to create resources

2. **Configure Variables**

   Edit `terraform.tfvars` or set the following variables:
   - `subscription_id` (required)
   - `location` (default: `eastus`)

3. **Initialize Terraform**

   ```sh
   terraform init
   ```

4. **Plan the Deployment**

   ```sh
   terraform plan
   ```

5. **Apply the Configuration**

   ```sh
   terraform apply
   ```

## Variables

See [`variables.tf`](iac/variables.tf) for all configurable variables.

## Outputs

- API keys and endpoint for Form Recognizer are stored as secrets in Azure Key Vault:
  - `api-key-1-fr`
  - `api-key-2-fr`
  - `api-endpoint-fr`
- Translator configuration is also stored in Azure Key Vault:
  - `translator-key`
  - `translator-endpoint`
  - `translator-region`

## Notes

- Resource tags are set via the `local.tags` local variable.
- Storage containers for images and text output are defined but commented out in [`storage.tf`](iac/storage.tf).
- Follows [Azure Terraform best practices](https://learn.microsoft.com/en-us/azure/developer/terraform/best-practices).