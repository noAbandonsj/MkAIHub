from fastapi.testclient import TestClient


def test_health_endpoint(client: TestClient) -> None:
    response = client.get("/api/health")

    assert response.status_code == 200
    assert response.json() == {
        "status": "ok",
        "service": "MkAIHub Test",
        "environment": "test",
        "version": "0.1.0",
    }


def test_unknown_api_uses_uniform_error_response(client: TestClient) -> None:
    response = client.get("/api/does-not-exist")

    assert response.status_code == 404
    assert response.json() == {"code": "NOT_FOUND", "message": "API route not found"}
