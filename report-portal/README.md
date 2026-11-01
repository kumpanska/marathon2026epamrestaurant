# API Autotests with Report Portal Integration

A learning project: API tests written with pytest, reports sent to Report Portal (hosted on AWS EC2), and a CI pipeline in GitHub Actions running on a self-hosted runner on the same EC2 instance.

## Architecture

- **Report Portal** runs with Docker Compose on AWS EC2 (Ubuntu 22.04, `m7i-flex.large`, Elastic IP).
- **Tests** are written in Python (pytest, requests) and verify the public jsonplaceholder API.
- **CI/CD**: GitHub Actions; the job runs on a self-hosted runner installed on the EC2 instance.
- **Reporting**: pytest-reportportal sends results to the `autotests` project in Report Portal.

## SDLC Stages

commit/push -> build (install dependencies) -> test (pytest) -> report (Report Portal)

The pipeline runs on push to `main`, on pull requests, on a daily schedule (06:00 UTC), and manually.

## Deploying Report Portal on AWS

1. Create an EC2 instance (Ubuntu 22.04, 2 vCPU, 8 GB RAM, 30 GB gp3 disk) and allocate an Elastic IP.
2. In the Security Group, open ports 22 and 8080 for your own IP only.
3. Install Docker and Docker Compose.
4. Set the kernel parameter required by OpenSearch: `sudo sysctl -w vm.max_map_count=262144`.
5. Download the official `docker-compose.yml` from the reportportal/reportportal repository, set `RP_INITIAL_ADMIN_PASSWORD`, and start the stack:
   `docker compose -p reportportal up -d`
6. Open `http://<EIP>:8080`, create the `autotests` project and a dedicated user, then generate an API key.

## Running Tests Locally

```powershell
pip install -r requirements.txt
$env:RP_API_KEY = "<your_api_key>"
pytest --reportportal -o rp_endpoint=http://<EIP>:8080 -o rp_project=autotests -o rp_api_key=$env:RP_API_KEY
```

To run without reporting to Report Portal, simply use `pytest`.

## GitHub Secrets

Configured under Settings -> Secrets and variables -> Actions:

- `RP_API_KEY`: API key of the Report Portal user;
- `RP_ENDPOINT`: Report Portal address (`http://localhost:8080`, since the runner is on the same EC2 instance).

No keys are stored in the repository.

## Project Structure

```
.
├── .github/workflows/tests.yml   # pipeline
├── test_api.py                   # tests
├── pytest.ini                    # pytest settings
├── requirements.txt              # dependencies
└── README.md
```

## Note

One test (`test_intentional_fail`) fails on purpose so that both passed and failed results are visible in the Report Portal dashboards.
